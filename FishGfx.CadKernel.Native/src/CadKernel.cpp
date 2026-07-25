#include "FishGfxCadKernel.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <limits>
#include <memory>
#include <mutex>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

#if defined(_WIN32)
#if !defined(NOMINMAX)
#define NOMINMAX
#endif
#if !defined(WIN32_LEAN_AND_MEAN)
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#endif

#include <BRepAdaptor_Curve.hxx>
#include <BRepAdaptor_Surface.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <BRepAlgoAPI_Common.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepClass3d_SolidClassifier.hxx>
#include <BRepClass_FaceClassifier.hxx>
#include <BOPAlgo_GlueEnum.hxx>
#include <BRepBndLib.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeSolid.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepBuilderAPI_Sewing.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepGProp.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepOffsetAPI_MakePipe.hxx>
#include <BRepOffsetAPI_MakeOffset.hxx>
#include <BRepOffsetAPI_ThruSections.hxx>
#include <BRepPrimAPI_MakeCylinder.hxx>
#include <BRepPrimAPI_MakeHalfSpace.hxx>
#include <BRepTools.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <BRep_Builder.hxx>
#include <BRep_Tool.hxx>
#include <BinXCAFDrivers.hxx>
#include <Bnd_Box.hxx>
#include <GC_MakeArcOfCircle.hxx>
#include <GProp_GProps.hxx>
#include <GCPnts_AbscissaPoint.hxx>
#include <GeomAbs_CurveType.hxx>
#include <GeomAbs_SurfaceType.hxx>
#include <GeomAbs_JoinType.hxx>
#include <GeomAdaptor_Curve.hxx>
#include <GeomAPI_ProjectPointOnCurve.hxx>
#include <GeomFill_Trihedron.hxx>
#include <Geom_BezierCurve.hxx>
#include <Geom_Curve.hxx>
#include <Interface_Static.hxx>
#include <math_DirectPolynomialRoots.hxx>
#include <NCollection_List.hxx>
#include <NCollection_Array1.hxx>
#include <NCollection_Sequence.hxx>
#include <PCDM_ReaderStatus.hxx>
#include <PCDM_StoreStatus.hxx>
#include <Poly.hxx>
#include <Poly_Triangle.hxx>
#include <Poly_Triangulation.hxx>
#include <Precision.hxx>
#include <STEPCAFControl_Reader.hxx>
#include <STEPCAFControl_Writer.hxx>
#include <ShapeFix_Shape.hxx>
#include <ShapeFix_Shell.hxx>
#include <Standard_Failure.hxx>
#include <TCollection_ExtendedString.hxx>
#include <TDataStd_Name.hxx>
#include <TDF_ChildIterator.hxx>
#include <TDocStd_Document.hxx>
#include <TNaming_Selector.hxx>
#include <TopAbs_Orientation.hxx>
#include <TopExp.hxx>
#include <TopExp_Explorer.hxx>
#include <TopLoc_Location.hxx>
#include <TopoDS.hxx>
#include <TopoDS_Compound.hxx>
#include <TopoDS_Edge.hxx>
#include <TopoDS_Face.hxx>
#include <TopoDS_Shape.hxx>
#include <TopoDS_Vertex.hxx>
#include <TopoDS_Wire.hxx>
#include <XCAFApp_Application.hxx>
#include <XCAFDoc_DocumentTool.hxx>
#include <XCAFDoc_Editor.hxx>
#include <XCAFDoc_ShapeTool.hxx>
#include <gp_Ax2.hxx>
#include <gp_Ax3.hxx>
#include <gp_Circ.hxx>
#include <gp_Dir.hxx>
#include <gp_Pln.hxx>
#include <gp_Pnt.hxx>
#include <gp_Quaternion.hxx>
#include <gp_Trsf.hxx>
#include <gp_Vec.hxx>

namespace
{
thread_local std::string last_error;
constexpr double pi = 3.14159265358979323846;

void append_native_log(const std::string& level, const std::string& message) noexcept
{
	try
	{
#if defined(_WIN32)
		DWORD required_length = GetEnvironmentVariableW(
			L"FGCAD_LOG_PATH",
			nullptr,
			0);
		if (required_length == 0)
		{
			return;
		}
		std::vector<wchar_t> configured_path(required_length);
		if (GetEnvironmentVariableW(
			L"FGCAD_LOG_PATH",
			configured_path.data(),
			static_cast<DWORD>(configured_path.size())) == 0)
		{
			return;
		}
		std::filesystem::path path(configured_path.data());
#else
		const char* configured_path = std::getenv("FGCAD_LOG_PATH");
		if (configured_path == nullptr || configured_path[0] == '\0')
		{
			return;
		}
		std::filesystem::path path(configured_path);
#endif
		static std::mutex log_mutex;
		std::lock_guard<std::mutex> lock(log_mutex);
		auto now = std::chrono::system_clock::now();
		std::time_t time = std::chrono::system_clock::to_time_t(now);
		std::tm local_time{};
#if defined(_WIN32)
		localtime_s(&local_time, &time);
#else
		localtime_r(&time, &local_time);
#endif
		auto milliseconds = std::chrono::duration_cast<std::chrono::milliseconds>(
			now.time_since_epoch()) % 1000;
		std::ofstream stream(path, std::ios::app);
		if (!stream)
		{
			return;
		}
		stream
			<< std::put_time(&local_time, "%Y-%m-%dT%H:%M:%S")
			<< '.'
			<< std::setfill('0')
			<< std::setw(3)
			<< milliseconds.count()
			<< " [native/"
			<< level
			<< "] "
			<< message
			<< std::endl;
	}
	catch (...)
	{
		// Diagnostics must never turn a recoverable CAD failure into a process failure.
	}
}

struct topology_record
{
	fgcad_topology_info info{};
	TopoDS_Shape shape;
};

struct part_record
{
	std::string id;
	std::string name;
	TopoDS_Shape shape;
	gp_Trsf placement;
	std::vector<topology_record> topology;
	Handle(TDocStd_Document) source_document;
	TDF_Label source_root;
};

struct runner_source
{
	std::string id;
	fgcad_runner_feature feature{};
	std::vector<TopoDS_Face> faces;
	fgcad_geometry_source_kind kind{ FGCAD_SOURCE_RUNNER_NODE };
	std::string owner_id;
};

struct runner_record
{
	std::string id;
	std::string name;
	TopoDS_Shape shape;
	std::vector<runner_source> sources;
};

struct collector_record
{
	std::string id;
	std::string name;
	uint64_t generation_revision{};
	TopoDS_Shape shape;
	TopoDS_Shape wall_shape;
	std::vector<TopoDS_Shape> wall_component_shapes;
	std::vector<TopoDS_Shape> coupler_shapes;
	std::vector<runner_source> sources;
	std::vector<runner_source> collector_sources;
	std::vector<std::string> runner_ids;
	fgcad_collector_system_spec geometry_spec{};
	std::vector<fgcad_collector_inlet> inlet_specs;
	bool has_wall_cache{};
};

struct selector_record
{
	std::string id;
	std::string part_id;
	uint64_t topology_id{};
};

fgcad_point3 point(const gp_Pnt& value)
{
	return { value.X(), value.Y(), value.Z() };
}

fgcad_point3 direction(const gp_Dir& value)
{
	return { value.X(), value.Y(), value.Z() };
}

gp_Pnt point(const fgcad_point3& value)
{
	return gp_Pnt(value.x, value.y, value.z);
}

gp_Vec vector(const fgcad_point3& value)
{
	return gp_Vec(value.x, value.y, value.z);
}

gp_Dir unit(const fgcad_point3& value)
{
	return gp_Dir(value.x, value.y, value.z);
}

std::string require_text(const char* value, const char* parameter)
{
	if (value == nullptr || *value == '\0')
	{
		throw std::invalid_argument(std::string(parameter) + " cannot be empty.");
	}

	return value;
}

TCollection_ExtendedString extended(const std::string& value)
{
	return TCollection_ExtendedString(value.c_str(), true);
}

std::string encode_label_text(const std::string& value)
{
	static constexpr char digits[] = "0123456789ABCDEF";
	std::string result;
	result.reserve(value.size() * 2);
	for (unsigned char character : value)
	{
		result.push_back(digits[character >> 4]);
		result.push_back(digits[character & 0x0f]);
	}
	return result;
}

std::string decode_label_text(const std::string& value)
{
	auto digit = [](char character)
	{
		if (character >= '0' && character <= '9') return character - '0';
		if (character >= 'A' && character <= 'F') return character - 'A' + 10;
		if (character >= 'a' && character <= 'f') return character - 'a' + 10;
		return -1;
	};
	if (value.size() % 2 != 0)
	{
		throw std::invalid_argument("An encoded XCAF label field has an invalid length.");
	}
	std::string result;
	result.reserve(value.size() / 2);
	for (size_t index = 0; index < value.size(); index += 2)
	{
		int high = digit(value[index]);
		int low = digit(value[index + 1]);
		if (high < 0 || low < 0)
		{
			throw std::invalid_argument("An encoded XCAF label field contains invalid hexadecimal text.");
		}
		result.push_back(static_cast<char>((high << 4) | low));
	}
	return result;
}

gp_Trsf transform(const fgcad_transform& value)
{
	double length = std::sqrt(
		value.rotation.x * value.rotation.x
		+ value.rotation.y * value.rotation.y
		+ value.rotation.z * value.rotation.z
		+ value.rotation.w * value.rotation.w
	);

	if (!std::isfinite(length) || length <= 1.0e-12)
	{
		throw std::invalid_argument("Part rotation must be a finite non-zero quaternion.");
	}

	gp_Quaternion rotation(
		value.rotation.x / length,
		value.rotation.y / length,
		value.rotation.z / length,
		value.rotation.w / length
	);
	gp_Trsf result;
	result.SetTransformation(rotation, gp_Vec(
		value.translation.x,
		value.translation.y,
		value.translation.z
	));
	return result;
}

TopoDS_Shape placed(const part_record& part)
{
	return part.shape.Moved(TopLoc_Location(part.placement));
}

fgcad_topology_kind classify(const TopoDS_Shape& shape)
{
	if (shape.ShapeType() == TopAbs_FACE)
	{
		BRepAdaptor_Surface surface(TopoDS::Face(shape));
		return surface.GetType() == GeomAbs_Cylinder
			? FGCAD_TOPOLOGY_CYLINDRICAL_FACE
			: FGCAD_TOPOLOGY_FACE;
	}

	if (shape.ShapeType() == TopAbs_EDGE)
	{
		BRepAdaptor_Curve curve(TopoDS::Edge(shape));
		return curve.GetType() == GeomAbs_Circle
			? FGCAD_TOPOLOGY_CIRCULAR_EDGE
			: FGCAD_TOPOLOGY_EDGE;
	}

	return FGCAD_TOPOLOGY_UNKNOWN;
}

void rebuild_topology(part_record& part)
{
	part.topology.clear();
	uint64_t id = 1;

	for (TopExp_Explorer explorer(part.shape, TopAbs_FACE); explorer.More(); explorer.Next())
	{
		TopoDS_Face face = TopoDS::Face(explorer.Current());
		fgcad_topology_info info{};
		info.id = id++;
		info.kind = classify(face);

		if (info.kind == FGCAD_TOPOLOGY_CYLINDRICAL_FACE)
		{
			gp_Cylinder cylinder = BRepAdaptor_Surface(face).Cylinder();
			info.center = point(cylinder.Location());
			info.axis = direction(cylinder.Axis().Direction());
			info.radius = cylinder.Radius();
		}

		part.topology.push_back({ info, face });
	}

	for (TopExp_Explorer explorer(part.shape, TopAbs_EDGE); explorer.More(); explorer.Next())
	{
		TopoDS_Edge edge = TopoDS::Edge(explorer.Current());
		fgcad_topology_info info{};
		info.id = id++;
		info.kind = classify(edge);

		if (info.kind == FGCAD_TOPOLOGY_CIRCULAR_EDGE)
		{
			gp_Circ circle = BRepAdaptor_Curve(edge).Circle();
			info.center = point(circle.Location());
			info.axis = direction(circle.Axis().Direction());
			info.radius = circle.Radius();
		}

		part.topology.push_back({ info, edge });
	}

	for (TopExp_Explorer explorer(part.shape, TopAbs_FACE); explorer.More(); explorer.Next())
	{
		TopoDS_Face face = TopoDS::Face(explorer.Current());
		BRepAdaptor_Surface surface(face);

		if (surface.GetType() != GeomAbs_Plane)
		{
			continue;
		}

		gp_Pln plane = surface.Plane();
		gp_Dir axis = plane.Axis().Direction();

		if (face.Orientation() == TopAbs_REVERSED)
		{
			axis.Reverse();
		}

		TopoDS_Wire outer = BRepTools::OuterWire(face);

		for (TopExp_Explorer wires(face, TopAbs_WIRE); wires.More(); wires.Next())
		{
			TopoDS_Wire wire = TopoDS::Wire(wires.Current());

			if (wire.IsSame(outer))
			{
				continue;
			}

			BRepBuilderAPI_MakeFace opening(plane, wire, true);

			if (!opening.IsDone())
			{
				continue;
			}

			GProp_GProps properties;
			BRepGProp::SurfaceProperties(opening.Face(), properties);
			double area = std::abs(properties.Mass());

			if (!std::isfinite(area) || area <= 1e-9)
			{
				continue;
			}

			fgcad_topology_info info{};
			info.id = id++;
			info.kind = FGCAD_TOPOLOGY_CLOSED_PROFILE;
			info.center = point(properties.CentreOfMass());
			info.axis = direction(axis);
			Bnd_Box bounds;
			BRepBndLib::Add(wire, bounds, true);
			double x_min;
			double y_min;
			double z_min;
			double x_max;
			double y_max;
			double z_max;
			bounds.Get(x_min, y_min, z_min, x_max, y_max, z_max);
			gp_Pnt profile_center = properties.CentreOfMass();
			info.radius = 0;
			for (double x : { x_min, x_max })
			for (double y : { y_min, y_max })
			for (double z : { z_min, z_max })
			{
				info.radius = std::max(info.radius, profile_center.Distance(gp_Pnt(x, y, z)));
			}
			part.topology.push_back({ info, wire });
		}
	}
}

void import_step(part_record& part, const std::string& path)
{
	Handle(TDocStd_Document) source;
	Handle(XCAFApp_Application) application = XCAFApp_Application::GetApplication();
	BinXCAFDrivers::DefineFormat(application);
	application->NewDocument("BinXCAF", source);
	STEPCAFControl_Reader reader;

	if (!reader.Perform(path.c_str(), source))
	{
		throw std::runtime_error("STEPCAFControl_Reader failed to import the STEP document.");
	}

	Handle(XCAFDoc_ShapeTool) shapes = XCAFDoc_DocumentTool::ShapeTool(source->Main());
	NCollection_Sequence<TDF_Label> roots;
	shapes->GetFreeShapes(roots);

	if (roots.IsEmpty())
	{
		throw std::runtime_error("The STEP document contains no free shapes.");
	}

	TDF_Label root;

	if (roots.Length() == 1)
	{
		root = roots.Value(1);
	}
	else
	{
		BRep_Builder builder;
		TopoDS_Compound compound;
		builder.MakeCompound(compound);
		root = shapes->AddShape(compound, true);
		TDataStd_Name::Set(root, extended("Imported STEP assembly"));

		for (int index = 1; index <= roots.Length(); ++index)
		{
			shapes->AddComponent(root, roots.Value(index), TopLoc_Location());
		}

		shapes->UpdateAssemblies();
	}

	part.source_document = source;
	part.source_root = root;
	part.shape = shapes->GetShape(root);
}

Handle(TDocStd_Document) make_xcaf_document(
	const std::unordered_map<std::string, part_record>& parts,
	const std::unordered_map<std::string, runner_record>& runners,
	const std::unordered_map<std::string, selector_record>& selectors,
	const std::unordered_map<std::string, collector_record>& collectors,
	bool include_hidden_member_definitions
)
{
	Handle(TDocStd_Document) result;
	Handle(XCAFApp_Application) application = XCAFApp_Application::GetApplication();
	BinXCAFDrivers::DefineFormat(application);
	application->NewDocument("BinXCAF", result);
	Handle(XCAFDoc_ShapeTool) shapes = XCAFDoc_DocumentTool::ShapeTool(result->Main());

	std::unordered_map<std::string, TDF_Label> part_labels;
	BRep_Builder builder;
	TopoDS_Compound compound;
	builder.MakeCompound(compound);
	TDF_Label assembly = shapes->AddShape(compound, true);
	TDataStd_Name::Set(assembly, extended("FGASSEMBLY"));

	for (const auto& entry : parts)
	{
		const part_record& part = entry.second;
		TDF_Label definition;

		if (!part.source_document.IsNull() && !part.source_root.IsNull())
		{
			Handle(XCAFDoc_ShapeTool) source_shapes = XCAFDoc_DocumentTool::ShapeTool(
				part.source_document->Main()
			);
			NCollection_DataMap<TDF_Label, TDF_Label> label_map;
			definition = XCAFDoc_Editor::CloneShapeLabel(part.source_root, source_shapes, shapes, label_map);
		}
		else
		{
			definition = shapes->AddShape(part.shape, false);
		}

		TDF_Label label = shapes->AddComponent(assembly, definition, TopLoc_Location(part.placement));
		TDataStd_Name::Set(label, extended("FGPART:" + part.id + ":" + part.name));
		part_labels[part.id] = label;
	}

	for (const auto& entry : selectors)
	{
		const selector_record& selector = entry.second;
		auto part_found = parts.find(selector.part_id);

		if (part_found == parts.end())
		{
			continue;
		}

		const part_record& part = part_found->second;
		auto topology = std::find_if(part.topology.begin(), part.topology.end(), [&](const topology_record& item)
		{
			return item.info.id == selector.topology_id;
		});

		if (topology == part.topology.end())
		{
			continue;
		}

		TDF_Label label = part_labels.at(part.id).NewChild();
		TDataStd_Name::Set(label, extended(
			"FGSELECTOR:" + selector.id + ":" + selector.part_id + ":" + std::to_string(selector.topology_id)
		));
		TopoDS_Shape context = placed(part);
		TopoDS_Shape selection = topology->shape.Moved(TopLoc_Location(part.placement));

		if (!TNaming_Selector(label).Select(selection, context, true, true))
		{
			throw std::runtime_error("OCAF could not persist a topology selector.");
		}
	}

	std::vector<std::string> fused_runner_ids;
	for (const auto& entry : collectors)
	{
		fused_runner_ids.insert(
			fused_runner_ids.end(),
			entry.second.runner_ids.begin(),
			entry.second.runner_ids.end()
		);
	}

	for (const auto& entry : runners)
	{
		const runner_record& runner = entry.second;
		if (std::find(fused_runner_ids.begin(), fused_runner_ids.end(), runner.id)
			!= fused_runner_ids.end())
		{
			if (include_hidden_member_definitions && !runner.shape.IsNull())
			{
				TDF_Label definition = shapes->AddShape(runner.shape, false);
				TDataStd_Name::Set(
					definition,
					extended("FGRUNNERDEF:" + runner.id + ":" + runner.name)
				);
			}
			continue;
		}
		if (runner.shape.IsNull()) continue;
		TDF_Label definition = shapes->AddShape(runner.shape, false);
		TDF_Label label = shapes->AddComponent(assembly, definition, TopLoc_Location());
		TDataStd_Name::Set(label, extended("FGRUNNER:" + runner.id + ":" + runner.name));
	}

	for (const auto& entry : collectors)
	{
		const collector_record& collector = entry.second;
		if (collector.shape.IsNull()) continue;
		TDF_Label definition = shapes->AddShape(collector.shape, false);
		TDF_Label label = shapes->AddComponent(assembly, definition, TopLoc_Location());
		std::string members;
		for (size_t index = 0; index < collector.runner_ids.size(); ++index)
		{
			if (index != 0) members += ",";
			members += collector.runner_ids[index];
		}
		TDataStd_Name::Set(label, extended(
			"FGCOLLECTOR:V2:" + collector.id + ":"
				+ encode_label_text(collector.name) + ":" + members));
	}

	shapes->UpdateAssemblies();

	return result;
}

std::string label_name(const TDF_Label& label)
{
	Handle(TDataStd_Name) name;

	if (!label.FindAttribute(TDataStd_Name::GetID(), name))
	{
		return {};
	}

	TCollection_AsciiString ascii(name->Get(), '?');
	return ascii.ToCString();
}

std::vector<const runner_source*> face_sources(
	const TopoDS_Face& face,
	const std::vector<runner_source>& sources
)
{
	if (sources.empty())
	{
		return {};
	}

	std::vector<const runner_source*> result;
	for (const runner_source& source : sources)
	{
		for (const TopoDS_Face& source_face : source.faces)
		{
			if (face.IsSame(source_face))
			{
				result.push_back(&source);
				break;
			}
		}
	}
	return result;
}

std::vector<TopoDS_Face> shape_faces(const TopoDS_Shape& shape)
{
	std::vector<TopoDS_Face> faces;
	for (TopExp_Explorer explorer(shape, TopAbs_FACE); explorer.More(); explorer.Next())
	{
		faces.push_back(TopoDS::Face(explorer.Current()));
	}
	return faces;
}

template<typename operation_type>
void apply_boolean_history(operation_type& operation, std::vector<runner_source>& sources)
{
	for (runner_source& source : sources)
	{
		std::vector<TopoDS_Face> mapped;
		auto append_unique = [&](const NCollection_List<TopoDS_Shape>& values)
		{
			for (NCollection_List<TopoDS_Shape>::Iterator iterator(values);
				iterator.More();
				iterator.Next())
			{
				if (iterator.Value().ShapeType() != TopAbs_FACE)
				{
					continue;
				}
				TopoDS_Face candidate = TopoDS::Face(iterator.Value());
				if (std::none_of(
					mapped.begin(),
					mapped.end(),
					[&](const TopoDS_Face& existing)
					{
						return existing.IsSame(candidate);
					}))
				{
					mapped.push_back(candidate);
				}
			}
		};
		for (const TopoDS_Face& face : source.faces)
		{
			const NCollection_List<TopoDS_Shape>& modified = operation.Modified(face);
			const NCollection_List<TopoDS_Shape>& generated = operation.Generated(face);
			if (!modified.IsEmpty() || !generated.IsEmpty())
			{
				append_unique(modified);
				append_unique(generated);
			}
			else if (!operation.IsDeleted(face))
			{
				mapped.push_back(face);
			}
		}
		source.faces = std::move(mapped);
	}
}

void copy_id(char (&destination)[40], const std::string& value)
{
	std::memset(destination, 0, sizeof(destination));
	std::memcpy(destination, value.data(), std::min(value.size(), sizeof(destination) - 1));
}

template<typename action_type>
fgcad_status guarded(action_type&& action)
{
	try
	{
		last_error.clear();
		return action();
	}
	catch (const std::invalid_argument& error)
	{
		last_error = error.what();
		append_native_log("invalid-argument", last_error);
		return FGCAD_STATUS_INVALID_ARGUMENT;
	}
	catch (const Standard_Failure& error)
	{
		last_error = error.what();
		append_native_log("modeling-error", last_error);
		return FGCAD_STATUS_MODELING_FAILED;
	}
	catch (const std::exception& error)
	{
		last_error = error.what();
		append_native_log("error", last_error);
		return FGCAD_STATUS_INTERNAL_ERROR;
	}
	catch (...)
	{
		last_error = "Unknown native CAD failure.";
		append_native_log("fatal", last_error);
		return FGCAD_STATUS_INTERNAL_ERROR;
	}
}
}

struct fgcad_document
{
	std::unordered_map<std::string, part_record> parts;
	std::unordered_map<std::string, runner_record> runners;
	std::unordered_map<std::string, collector_record> collectors;
	std::unordered_map<std::string, runner_record> staged_runners;
	std::string staged_collector_id;
	uint64_t staged_generation_revision{};
	std::unordered_map<std::string, selector_record> selectors;
};

struct fgcad_tessellation
{
	std::vector<fgcad_mesh_vertex> vertices;
	std::vector<uint32_t> indices;
	std::vector<fgcad_face_range> faces;
	std::vector<fgcad_geometry_source_ref> sources;
	std::vector<fgcad_edge_range> edges;
	std::vector<fgcad_point3> edge_points;
	fgcad_point3 minimum{};
	fgcad_point3 maximum{};
};

namespace
{
part_record& find_part(fgcad_document& document, const std::string& id)
{
	auto found = document.parts.find(id);

	if (found == document.parts.end())
	{
		throw std::out_of_range("The requested CAD part was not found.");
	}

	return found->second;
}

template<typename action_type>
fgcad_status not_found_guarded(action_type&& action)
{
	return guarded([&]()
	{
		try
		{
			return action();
		}
		catch (const std::out_of_range& error)
		{
			last_error = error.what();
			return FGCAD_STATUS_NOT_FOUND;
		}
	});
}

std::unique_ptr<fgcad_tessellation> tessellate(
	const TopoDS_Shape& input,
	double linear_deflection,
	double angular_deflection,
	const std::vector<runner_source>& sources = {}
)
{
	if (input.IsNull())
	{
		throw std::out_of_range("There is no exact shape to tessellate.");
	}

	if (!(linear_deflection > 0) || !(angular_deflection > 0))
	{
		throw std::invalid_argument("Tessellation deflections must be greater than zero.");
	}

	BRepMesh_IncrementalMesh mesher(input, linear_deflection, false, angular_deflection, true);
	mesher.Perform();
	auto result = std::make_unique<fgcad_tessellation>();
	uint64_t topology_id = 1;

	for (TopExp_Explorer explorer(input, TopAbs_FACE); explorer.More(); explorer.Next(), ++topology_id)
	{
		TopoDS_Face face = TopoDS::Face(explorer.Current());
		TopLoc_Location location;
		Handle(Poly_Triangulation) triangulation = BRep_Tool::Triangulation(face, location);

		if (triangulation.IsNull())
		{
			continue;
		}

		if (!triangulation->HasNormals())
		{
			Poly::ComputeNormals(triangulation);
		}

		uint32_t vertex_base = static_cast<uint32_t>(result->vertices.size());
		uint32_t first_index = static_cast<uint32_t>(result->indices.size());

		for (int index = 1; index <= triangulation->NbNodes(); ++index)
		{
			gp_Pnt position = triangulation->Node(index).Transformed(location.Transformation());
			gp_Dir normal = triangulation->Normal(index).Transformed(location.Transformation());

			if (face.Orientation() == TopAbs_REVERSED)
			{
				normal.Reverse();
			}

			result->vertices.push_back({
				static_cast<float>(position.X()),
				static_cast<float>(position.Y()),
				static_cast<float>(position.Z()),
				static_cast<float>(normal.X()),
				static_cast<float>(normal.Y()),
				static_cast<float>(normal.Z()),
			});
		}

		for (int index = 1; index <= triangulation->NbTriangles(); ++index)
		{
			int a;
			int b;
			int c;
			triangulation->Triangle(index).Get(a, b, c);

			if (face.Orientation() == TopAbs_REVERSED)
			{
				std::swap(b, c);
			}

			result->indices.push_back(vertex_base + static_cast<uint32_t>(a - 1));
			result->indices.push_back(vertex_base + static_cast<uint32_t>(b - 1));
			result->indices.push_back(vertex_base + static_cast<uint32_t>(c - 1));
		}

		fgcad_face_range range{};
		range.topology_id = topology_id;
		range.first_index = first_index;
		range.index_count = static_cast<uint32_t>(result->indices.size()) - first_index;
		range.first_source = static_cast<uint32_t>(result->sources.size());
		for (const runner_source* source : face_sources(face, sources))
		{
			fgcad_geometry_source_ref reference{};
			reference.kind = source->kind;
			copy_id(reference.owner_id, source->owner_id);
			copy_id(reference.element_id, source->id);
			result->sources.push_back(reference);
		}
		range.source_count = static_cast<uint32_t>(result->sources.size()) - range.first_source;
		result->faces.push_back(range);
	}

	for (TopExp_Explorer explorer(input, TopAbs_EDGE); explorer.More(); explorer.Next(), ++topology_id)
	{
		TopoDS_Edge edge = TopoDS::Edge(explorer.Current());
		BRepAdaptor_Curve curve(edge);
		double first = curve.FirstParameter();
		double last = curve.LastParameter();

		if (!std::isfinite(first) || !std::isfinite(last))
		{
			continue;
		}

		fgcad_edge_range range{};
		range.topology_id = topology_id;
		range.kind = classify(edge);
		range.first_point = static_cast<uint32_t>(result->edge_points.size());
		int samples = range.kind == FGCAD_TOPOLOGY_CIRCULAR_EDGE ? 65 : 17;

		for (int index = 0; index < samples; ++index)
		{
			double parameter = first + (last - first) * index / (samples - 1);
			result->edge_points.push_back(point(curve.Value(parameter)));
		}

		range.point_count = static_cast<uint32_t>(result->edge_points.size()) - range.first_point;
		result->edges.push_back(range);
	}

	Bnd_Box bounds;
	BRepBndLib::Add(input, bounds);
	double x_min;
	double y_min;
	double z_min;
	double x_max;
	double y_max;
	double z_max;
	bounds.Get(x_min, y_min, z_min, x_max, y_max, z_max);
	result->minimum = { x_min, y_min, z_min };
	result->maximum = { x_max, y_max, z_max };
	return result;
}

#include "CadKernel.Curves.inl"
}

extern "C"
{
#include "CadKernel.EvaluationApi.inl"

#include "CadKernel.Documents.inl"

#include "CadKernel.Runners.inl"

#include "CadKernel.Collectors.inl"

#include "CadKernel.Tessellation.inl"

#include "CadKernel.Persistence.inl"


}
