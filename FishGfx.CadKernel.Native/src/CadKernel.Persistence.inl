// Versioned C ABI entry points for XCAF persistence and AP242 export.

fgcad_status fgcad_document_save_xcaf(fgcad_document* document, const char* path_utf8)
{
	return guarded([&]()
	{
		if (document == nullptr)
		{
			throw std::invalid_argument("The document cannot be null.");
		}

		std::string path = require_text(path_utf8, "path_utf8");
		Handle(TDocStd_Document) xcaf = make_xcaf_document(
			document->parts,
			document->runners,
			document->selectors,
			document->collectors,
			true
		);
		Handle(XCAFApp_Application) application = XCAFApp_Application::GetApplication();
		PCDM_StoreStatus status = application->SaveAs(xcaf, extended(path));
		application->Close(xcaf);

		if (status != PCDM_SS_OK)
		{
			last_error = "The XCAF binary document could not be saved.";
			return FGCAD_STATUS_IO_FAILED;
		}

		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_load_xcaf(fgcad_document* document, const char* path_utf8)
{
	return guarded([&]()
	{
		if (document == nullptr)
		{
			throw std::invalid_argument("The document cannot be null.");
		}

		Handle(TDocStd_Document) xcaf;
		Handle(XCAFApp_Application) application = XCAFApp_Application::GetApplication();
		BinXCAFDrivers::DefineFormat(application);
		PCDM_ReaderStatus status = application->Open(
			extended(require_text(path_utf8, "path_utf8")),
			xcaf
		);

		if (status != PCDM_RS_OK)
		{
			last_error = "The XCAF binary document could not be opened (status "
				+ std::to_string(static_cast<int>(status)) + ").";
			return FGCAD_STATUS_IO_FAILED;
		}

		Handle(XCAFDoc_ShapeTool) shapes = XCAFDoc_DocumentTool::ShapeTool(xcaf->Main());
		NCollection_Sequence<TDF_Label> roots;
		shapes->GetFreeShapes(roots);
		fgcad_document replacement;

		auto load_component = [&](const TDF_Label& label)
		{
			std::string name = label_name(label);
			TDF_Label referred;
			bool is_reference = XCAFDoc_ShapeTool::GetReferredShape(label, referred);
			TopoDS_Shape shape = shapes->GetShape(is_reference ? referred : label);
			gp_Trsf placement = XCAFDoc_ShapeTool::GetLocation(label).Transformation();

			if (name == "FGRUNNER" || name.rfind("FGRUNNER:", 0) == 0
				|| name.rfind("FGRUNNERDEF:", 0) == 0)
			{
				runner_record runner;
				if (name == "FGRUNNER")
				{
					runner.id = "legacy-runner";
					runner.name = "Runner 1";
				}
				else if (name.rfind("FGRUNNERDEF:", 0) == 0)
				{
					size_t separator = name.find(':', 12);
					runner.id = separator == std::string::npos
						? name.substr(12)
						: name.substr(12, separator - 12);
					runner.name = separator == std::string::npos
						? "Runner"
						: name.substr(separator + 1);
				}
				else
				{
					size_t separator = name.find(':', 9);
					runner.id = separator == std::string::npos ? name.substr(9) : name.substr(9, separator - 9);
					runner.name = separator == std::string::npos ? "Runner" : name.substr(separator + 1);
				}
				runner.shape = shape.Moved(TopLoc_Location(placement));
				replacement.runners[runner.id] = std::move(runner);
				return;
			}

			if (name.rfind("FGCOLLECTOR:", 0) == 0)
			{
				std::string fields = name.substr(12);
				bool version_two = fields.rfind("V2:", 0) == 0;
				if (version_two)
				{
					fields = fields.substr(3);
				}
				size_t first = fields.find(':');
				size_t second = fields.find(':', first == std::string::npos ? first : first + 1);
				collector_record collector;
				collector.id = first == std::string::npos ? fields : fields.substr(0, first);
				collector.name = first == std::string::npos
					? "Collector"
					: version_two
						? decode_label_text(fields.substr(
							first + 1,
							second == std::string::npos
								? std::string::npos
								: second - first - 1))
						: fields.substr(first + 1, second == std::string::npos
							? std::string::npos
							: second - first - 1);
				if (second != std::string::npos)
				{
					std::string members = fields.substr(second + 1);
					size_t begin = 0;
					while (begin < members.size())
					{
						size_t comma = members.find(',', begin);
						collector.runner_ids.push_back(members.substr(
							begin,
							comma == std::string::npos ? std::string::npos : comma - begin));
						if (comma == std::string::npos) break;
						begin = comma + 1;
					}
				}
				collector.shape = shape.Moved(TopLoc_Location(placement));
				replacement.collectors[collector.id] = std::move(collector);
				return;
			}

			if (name.rfind("FGPART:", 0) != 0)
			{
				return;
			}

			size_t separator = name.find(':', 7);
			part_record part;
			part.id = separator == std::string::npos ? name.substr(7) : name.substr(7, separator - 7);
			part.name = separator == std::string::npos ? "Part" : name.substr(separator + 1);
			part.shape = shape;
			part.placement = placement;
			part.source_document = xcaf;
			part.source_root = is_reference ? referred : label;
			rebuild_topology(part);
			replacement.parts[part.id] = std::move(part);

			for (TDF_ChildIterator child(label, false); child.More(); child.Next())
			{
				std::string selector_name = label_name(child.Value());

				if (selector_name.rfind("FGSELECTOR:", 0) != 0)
				{
					continue;
				}

				std::string fields = selector_name.substr(11);
				size_t first = fields.find(':');
				size_t second = fields.find(':', first == std::string::npos ? first : first + 1);

				if (first == std::string::npos || second == std::string::npos)
				{
					continue;
				}

				selector_record selector;
				selector.id = fields.substr(0, first);
				selector.part_id = fields.substr(first + 1, second - first - 1);
				selector.topology_id = std::stoull(fields.substr(second + 1));
				replacement.selectors[selector.id] = std::move(selector);
			}
		};

		for (int index = 1; index <= roots.Length(); ++index)
		{
			TDF_Label root = roots.Value(index);

			if (label_name(root) == "FGASSEMBLY")
			{
				NCollection_Sequence<TDF_Label> components;
				XCAFDoc_ShapeTool::GetComponents(root, components, false);

				for (int component_index = 1; component_index <= components.Length(); ++component_index)
				{
					load_component(components.Value(component_index));
				}
			}
			else
			{
				load_component(root);
			}
		}

		document->parts = std::move(replacement.parts);
		document->runners = std::move(replacement.runners);
		document->selectors = std::move(replacement.selectors);
		document->collectors = std::move(replacement.collectors);

		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_export_step_ap242(fgcad_document* document, const char* path_utf8)
{
	return guarded([&]()
	{
		if (document == nullptr || document->runners.empty() && document->collectors.empty()
			|| std::any_of(document->runners.begin(), document->runners.end(), [](const auto& item)
			{
				return item.second.shape.IsNull();
			}))
		{
			throw std::invalid_argument("A valid exact runner is required before STEP export.");
		}

		Handle(TDocStd_Document) xcaf = make_xcaf_document(
			document->parts,
			document->runners,
			document->selectors,
			document->collectors,
			false
		);
		Interface_Static::SetCVal("write.step.schema", "AP242DIS");
		STEPCAFControl_Writer writer;

		if (!writer.Perform(xcaf, require_text(path_utf8, "path_utf8").c_str()))
		{
			last_error = "STEPCAFControl_Writer failed to export the AP242 assembly.";
			return FGCAD_STATUS_IO_FAILED;
		}

		return FGCAD_STATUS_OK;
	});
}
