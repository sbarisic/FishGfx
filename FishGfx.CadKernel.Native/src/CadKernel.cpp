#include "FishGfxCadKernel.h"

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <iostream>
#include <limits>
#include <memory>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

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
	std::vector<runner_source> sources;
	std::vector<std::string> runner_ids;
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
		return FGCAD_STATUS_INVALID_ARGUMENT;
	}
	catch (const Standard_Failure& error)
	{
		last_error = error.what();
		return FGCAD_STATUS_MODELING_FAILED;
	}
	catch (const std::exception& error)
	{
		last_error = error.what();
		return FGCAD_STATUS_INTERNAL_ERROR;
	}
	catch (...)
	{
		last_error = "Unknown native CAD failure.";
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

struct interval
{
	double low;
	double high;
};

interval product(interval left, interval right)
{
	const double values[] = {
		left.low * right.low,
		left.low * right.high,
		left.high * right.low,
		left.high * right.high
	};
	return {
		*std::min_element(std::begin(values), std::end(values)),
		*std::max_element(std::begin(values), std::end(values))
	};
}

interval subtract(interval left, interval right)
{
	return { left.low - right.high, left.high - right.low };
}

interval component_bounds(const gp_Vec* values, size_t count, int component)
{
	double minimum = std::numeric_limits<double>::infinity();
	double maximum = -std::numeric_limits<double>::infinity();
	for (size_t index = 0; index < count; ++index)
	{
		const double value = component == 0 ? values[index].X()
			: component == 1 ? values[index].Y()
			: values[index].Z();
		minimum = std::min(minimum, value);
		maximum = std::max(maximum, value);
	}
	return { minimum, maximum };
}

double minimum_absolute(interval value)
{
	if (value.low <= 0 && value.high >= 0) return 0;
	return std::min(std::abs(value.low), std::abs(value.high));
}

double maximum_absolute(interval value)
{
	return std::max(std::abs(value.low), std::abs(value.high));
}

template<size_t count>
void subdivide_bernstein(
	const std::array<gp_Vec, count>& source,
	std::array<gp_Vec, count>& left,
	std::array<gp_Vec, count>& right
)
{
	std::array<std::array<gp_Vec, count>, count> levels{};
	levels[0] = source;
	left[0] = source[0];
	right[count - 1] = source[count - 1];
	for (size_t level = 1; level < count; ++level)
	{
		for (size_t index = 0; index < count - level; ++index)
		{
			levels[level][index] = (levels[level - 1][index] + levels[level - 1][index + 1]) * 0.5;
		}
		left[level] = levels[level][0];
		right[count - level - 1] = levels[level][count - level - 1];
	}
}

bool certify_curvature(
	const std::array<gp_Vec, 3>& derivative,
	const std::array<gp_Vec, 2>& second_derivative,
	double required_radius,
	int depth,
	double& minimum_radius
)
{
	interval dx = component_bounds(derivative.data(), derivative.size(), 0);
	interval dy = component_bounds(derivative.data(), derivative.size(), 1);
	interval dz = component_bounds(derivative.data(), derivative.size(), 2);
	double speed_lower = std::sqrt(
		std::pow(minimum_absolute(dx), 2)
		+ std::pow(minimum_absolute(dy), 2)
		+ std::pow(minimum_absolute(dz), 2)
	);
	interval ex = component_bounds(second_derivative.data(), second_derivative.size(), 0);
	interval ey = component_bounds(second_derivative.data(), second_derivative.size(), 1);
	interval ez = component_bounds(second_derivative.data(), second_derivative.size(), 2);
	interval cross_x = subtract(product(dy, ez), product(dz, ey));
	interval cross_y = subtract(product(dz, ex), product(dx, ez));
	interval cross_z = subtract(product(dx, ey), product(dy, ex));
	double cross_upper = std::sqrt(
		std::pow(maximum_absolute(cross_x), 2)
		+ std::pow(maximum_absolute(cross_y), 2)
		+ std::pow(maximum_absolute(cross_z), 2)
	);
	double radius_lower;
	if (cross_upper == 0)
	{
		radius_lower = std::numeric_limits<double>::infinity();
	}
	else if (speed_lower == 0)
	{
		radius_lower = 0;
	}
	else
	{
		const double logarithmic_radius = 3.0 * std::log(speed_lower) - std::log(cross_upper);
		const double maximum_logarithm = std::log(std::numeric_limits<double>::max());
		radius_lower = logarithmic_radius >= maximum_logarithm
			? std::numeric_limits<double>::infinity()
			: std::exp(logarithmic_radius);
	}

	if (radius_lower > required_radius)
	{
		minimum_radius = std::min(minimum_radius, radius_lower);
		return true;
	}
	if (depth >= 24)
	{
		return false;
	}

	std::array<gp_Vec, 3> derivative_left;
	std::array<gp_Vec, 3> derivative_right;
	std::array<gp_Vec, 2> second_left;
	std::array<gp_Vec, 2> second_right;
	subdivide_bernstein(derivative, derivative_left, derivative_right);
	subdivide_bernstein(second_derivative, second_left, second_right);
	return certify_curvature(
		derivative_left, second_left, required_radius, depth + 1, minimum_radius)
		&& certify_curvature(
			derivative_right, second_right, required_radius, depth + 1, minimum_radius);
}

double speed_squared(const gp_Vec& a, const gp_Vec& b, const gp_Vec& c, double parameter)
{
	gp_Vec derivative = (a * parameter + b) * parameter + c;
	return derivative.SquareMagnitude();
}

double coordinate(const gp_Vec& value, int index)
{
	return index == 0 ? value.X() : index == 1 ? value.Y() : value.Z();
}

bool has_cubic_self_intersection(
	const gp_Vec& cubic,
	const gp_Vec& quadratic,
	const gp_Vec& linear,
	double scale
)
{
	double best_determinant = 0;
	int first_component = 0;
	int second_component = 1;
	for (int first = 0; first < 3; ++first)
	{
		for (int second = first + 1; second < 3; ++second)
		{
			double determinant = coordinate(cubic, first) * coordinate(quadratic, second)
				- coordinate(cubic, second) * coordinate(quadratic, first);
			if (std::abs(determinant) > std::abs(best_determinant))
			{
				best_determinant = determinant;
				first_component = first;
				second_component = second;
			}
		}
	}
	double coefficient_scale = std::max({
		cubic.Magnitude(),
		quadratic.Magnitude(),
		linear.Magnitude(),
		scale,
		1.0
	});
	if (std::abs(best_determinant) <= coefficient_scale * coefficient_scale * 1.0e-14)
	{
		return false;
	}

	double ai = coordinate(cubic, first_component);
	double aj = coordinate(cubic, second_component);
	double bi = coordinate(quadratic, first_component);
	double bj = coordinate(quadratic, second_component);
	double ci = coordinate(linear, first_component);
	double cj = coordinate(linear, second_component);
	double sum = (aj * ci - ai * cj) / best_determinant;
	double square_sum_minus_product = (bi * cj - bj * ci) / best_determinant;
	double product_value = sum * sum - square_sum_minus_product;
	double discriminant = sum * sum - 4.0 * product_value;
	if (discriminant <= 1.0e-14)
	{
		return false;
	}
	double root = std::sqrt(discriminant);
	double first_parameter = (sum - root) * 0.5;
	double second_parameter = (sum + root) * 0.5;
	if (first_parameter < -1.0e-12 || second_parameter > 1.0 + 1.0e-12
		|| second_parameter - first_parameter <= 1.0e-10)
	{
		return false;
	}
	gp_Vec residual = cubic * square_sum_minus_product + quadratic * sum + linear;
	return residual.Magnitude() <= coefficient_scale * 1.0e-9;
}

Handle(Geom_BezierCurve) make_bezier(
	const fgcad_frame& entry_frame,
	const fgcad_point3& control1,
	const fgcad_point3& control2,
	const fgcad_point3& end
)
{
	NCollection_Array1<gp_Pnt> poles(1, 4);
	poles.SetValue(1, point(entry_frame.origin));
	poles.SetValue(2, point(control1));
	poles.SetValue(3, point(control2));
	poles.SetValue(4, point(end));
	return new Geom_BezierCurve(poles);
}

fgcad_frame transport_frame(
	const TopoDS_Wire& spine,
	const fgcad_frame& entry_frame,
	const gp_Pnt& exit_origin,
	const gp_Dir& exit_tangent
)
{
	gp_Pnt entry_origin = point(entry_frame.origin);
	gp_Pnt profile_end = entry_origin.Translated(vector(entry_frame.normal));
	TopoDS_Edge profile = BRepBuilderAPI_MakeEdge(entry_origin, profile_end).Edge();
	BRepOffsetAPI_MakePipe transport(
		spine,
		profile,
		GeomFill_IsDiscreteTrihedron,
		true
	);
	if (!transport.IsDone())
	{
		throw std::runtime_error("Open CASCADE could not transport the cubic Bezier span frame.");
	}
	TopoDS_Shape last = transport.LastShape();
	std::vector<gp_Pnt> transported_points;
	for (TopExp_Explorer explorer(last, TopAbs_VERTEX); explorer.More(); explorer.Next())
	{
		transported_points.push_back(BRep_Tool::Pnt(TopoDS::Vertex(explorer.Current())));
	}
	if (transported_points.size() < 2)
	{
		throw std::runtime_error("Open CASCADE did not return a transported endpoint section.");
	}
	auto origin_point = std::min_element(
		transported_points.begin(),
		transported_points.end(),
		[&](const gp_Pnt& left, const gp_Pnt& right)
		{
			return left.SquareDistance(exit_origin) < right.SquareDistance(exit_origin);
		}
	);
	auto normal_point = std::max_element(
		transported_points.begin(),
		transported_points.end(),
		[&](const gp_Pnt& left, const gp_Pnt& right)
		{
			return left.SquareDistance(*origin_point) < right.SquareDistance(*origin_point);
		}
	);
	gp_Vec normal(*origin_point, *normal_point);
	normal -= gp_Vec(exit_tangent) * normal.Dot(exit_tangent);
	if (normal.SquareMagnitude() <= Precision::SquareConfusion())
	{
		throw std::runtime_error("The transported cubic Bezier span frame is singular.");
	}
	fgcad_frame result{};
	result.origin = point(exit_origin);
	result.tangent = direction(exit_tangent);
	result.normal = direction(gp_Dir(normal));
	return result;
}

fgcad_bezier_evaluation evaluate_cubic_bezier_internal(
	const fgcad_frame& entry_frame,
	const fgcad_point3& control1,
	const fgcad_point3& control2,
	const fgcad_point3& end,
	double outer_radius
)
{
	const fgcad_point3 points[] = {
		entry_frame.origin,
		control1,
		control2,
		end
	};
	for (const fgcad_point3& value : points)
	{
		if (!std::isfinite(value.x) || !std::isfinite(value.y) || !std::isfinite(value.z))
		{
			throw std::invalid_argument("Cubic Bezier control points must be finite.");
		}
	}
	if (!(outer_radius > 0) || !std::isfinite(outer_radius))
	{
		throw std::invalid_argument("The active outer profile radius must be positive and finite.");
	}

	gp_Pnt p0 = point(entry_frame.origin);
	gp_Pnt p1 = point(control1);
	gp_Pnt p2 = point(control2);
	gp_Pnt p3 = point(end);
	const double control_polygon_length = p0.Distance(p1) + p1.Distance(p2) + p2.Distance(p3);
	if (!(p0.Distance(p1) > Precision::Confusion()))
	{
		throw std::invalid_argument("The cubic Bezier start handle must be longer than kernel confusion.");
	}
	if (!(p2.Distance(p3) > Precision::Confusion()))
	{
		throw std::invalid_argument("The cubic Bezier exit handle must be non-zero.");
	}

	// B'(t) = a*t^2 + b*t + c.  Extrema of |B'|^2 are roots of a cubic.
	gp_Vec a = (gp_Vec(p0.XYZ()) - gp_Vec(p1.XYZ()) * 3.0
		+ gp_Vec(p2.XYZ()) * 3.0 - gp_Vec(p3.XYZ())) * -3.0;
	gp_Vec b = (gp_Vec(p0.XYZ()) - gp_Vec(p1.XYZ()) * 2.0 + gp_Vec(p2.XYZ())) * 6.0;
	gp_Vec c(p0, p1);
	c *= 3.0;
	if (has_cubic_self_intersection(a / 3.0, b / 2.0, c, control_polygon_length))
	{
		throw std::invalid_argument("The cubic Bezier centreline self-intersects.");
	}
	const double quartic[] = {
		a.Dot(a),
		2.0 * a.Dot(b),
		b.Dot(b) + 2.0 * a.Dot(c),
		2.0 * b.Dot(c),
		c.Dot(c)
	};
	math_DirectPolynomialRoots roots(
		4.0 * quartic[0],
		3.0 * quartic[1],
		2.0 * quartic[2],
		quartic[3]
	);
	if (!roots.IsDone())
	{
		throw std::runtime_error("The cubic Bezier derivative extrema could not be solved.");
	}
	double minimum_speed_squared = std::min(
		speed_squared(a, b, c, 0),
		speed_squared(a, b, c, 1)
	);
	if (!roots.InfiniteRoots())
	{
		for (int index = 1; index <= roots.NbSolutions(); ++index)
		{
			double parameter = roots.Value(index);
			if (parameter >= 0 && parameter <= 1)
			{
				minimum_speed_squared = std::min(
					minimum_speed_squared,
					speed_squared(a, b, c, parameter)
				);
			}
		}
	}
	const double speed_tolerance = std::max(
		Precision::Confusion(),
		control_polygon_length * 1.0e-9
	);
	if (minimum_speed_squared <= speed_tolerance * speed_tolerance)
	{
		throw std::invalid_argument("The cubic Bezier contains a cusp or singular derivative.");
	}

	std::array<gp_Vec, 3> derivative = {
		gp_Vec(p0, p1) * 3.0,
		gp_Vec(p1, p2) * 3.0,
		gp_Vec(p2, p3) * 3.0
	};
	std::array<gp_Vec, 2> second_derivative = {
		(derivative[1] - derivative[0]) * 2.0,
		(derivative[2] - derivative[1]) * 2.0
	};
	double minimum_radius = std::numeric_limits<double>::infinity();
	if (!certify_curvature(
		derivative,
		second_derivative,
		outer_radius + Precision::Confusion(),
		0,
		minimum_radius))
	{
		throw std::invalid_argument(
			"The cubic Bezier curvature cannot be certified for the active outer profile radius.");
	}

	Handle(Geom_BezierCurve) curve = make_bezier(entry_frame, control1, control2, end);
	GeomAdaptor_Curve adaptor(curve);
	const double length_tolerance = std::max(
		Precision::Confusion(),
		control_polygon_length * 1.0e-10
	);
	const double length = GCPnts_AbscissaPoint::Length(adaptor, length_tolerance);
	if (!std::isfinite(length) || !(length > Precision::Confusion()))
	{
		throw std::runtime_error("The tolerance-controlled cubic Bezier length could not be computed.");
	}

	gp_Vec tangent(p2, p3);
	fgcad_bezier_evaluation result{};
	result.exit_frame = transport_frame(
		BRepBuilderAPI_MakeWire(BRepBuilderAPI_MakeEdge(curve).Edge()).Wire(),
		entry_frame,
		p3,
		gp_Dir(tangent)
	);
	result.length = length;
	result.minimum_radius = minimum_radius;
	return result;
}
}

extern "C"
{
uint32_t fgcad_api_version(void)
{
	return 5;
}

const char* fgcad_last_error(void)
{
	return last_error.c_str();
}

fgcad_status fgcad_evaluate_cubic_bezier(
	const fgcad_frame* entry_frame,
	const fgcad_point3* control1,
	const fgcad_point3* control2,
	const fgcad_point3* end,
	double outer_radius,
	fgcad_bezier_evaluation* evaluation
)
{
	return guarded([&]()
	{
		if (entry_frame == nullptr || control1 == nullptr || control2 == nullptr
			|| end == nullptr || evaluation == nullptr)
		{
			throw std::invalid_argument("Cubic Bezier evaluation arguments cannot be null.");
		}
		*evaluation = evaluate_cubic_bezier_internal(
			*entry_frame, *control1, *control2, *end, outer_radius);
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_evaluate_runner_features(
	const fgcad_frame* start_frame,
	const fgcad_runner_profile* start_profile,
	const fgcad_runner_feature_spec* specifications,
	size_t specification_count,
	fgcad_runner_feature* evaluated_features,
	size_t evaluated_capacity,
	size_t* evaluated_count
)
{
	return guarded([&]()
	{
		if (start_frame == nullptr || start_profile == nullptr || specifications == nullptr
			|| evaluated_features == nullptr || evaluated_count == nullptr)
		{
			throw std::invalid_argument("Runner feature evaluation arguments cannot be null.");
		}
		*evaluated_count = 0;
		if (specification_count == 0 || evaluated_capacity < specification_count)
		{
			throw std::invalid_argument("The evaluated feature array has insufficient capacity.");
		}

		fgcad_frame frame = *start_frame;
		fgcad_runner_profile profile = *start_profile;
		std::unique_ptr<BRepBuilderAPI_MakeWire> transported_span;
		fgcad_frame transported_span_entry{};
		for (size_t index = 0; index < specification_count; ++index)
		{
			const fgcad_runner_feature_spec& specification = specifications[index];
			const bool transition = specification.kind == FGCAD_FEATURE_LOFT_TRANSITION
				|| specification.kind == FGCAD_FEATURE_CLOCKING_TRANSITION;
			if (!transition && !transported_span)
			{
				size_t span_end = index;
				bool contains_bezier = false;
				while (span_end < specification_count
					&& specifications[span_end].kind != FGCAD_FEATURE_LOFT_TRANSITION
					&& specifications[span_end].kind != FGCAD_FEATURE_CLOCKING_TRANSITION)
				{
					contains_bezier = contains_bezier
						|| specifications[span_end].kind == FGCAD_FEATURE_CUBIC_BEZIER;
					++span_end;
				}
				if (contains_bezier)
				{
					transported_span = std::make_unique<BRepBuilderAPI_MakeWire>();
					transported_span_entry = frame;
				}
			}
			fgcad_runner_feature feature{};
			feature.kind = specification.kind;
			copy_id(feature.source_node_id, specification.source_node_id);
			feature.entry_frame = frame;
			feature.input_profile = profile;
			feature.output_profile = profile;
			feature.rotation_radians = specification.rotation_radians;

			gp_Pnt origin = point(frame.origin);
			gp_Dir tangent = unit(frame.tangent);
			gp_Dir normal = unit(frame.normal);
			if (specification.kind == FGCAD_FEATURE_STRAIGHT)
			{
				if (!(specification.length > 0) || !std::isfinite(specification.length))
				{
					throw std::invalid_argument("Straight length must be positive and finite.");
				}
				frame.origin = point(origin.Translated(gp_Vec(tangent) * specification.length));
				feature.length = specification.length;
			}
			else if (specification.kind == FGCAD_FEATURE_BEND)
			{
				if (!(specification.radius > 0) || !(specification.sweep_radians > 0)
					|| specification.sweep_radians > pi || !std::isfinite(specification.rotation_radians))
				{
					throw std::invalid_argument("Bend radius, angle, or rotation is invalid.");
				}
				double outer_radius = profile.kind == FGCAD_PROFILE_CIRCULAR
					? profile.outer_diameter * 0.5
					: profile.equivalent_radius + profile.wall_thickness;
				if (!(specification.radius > outer_radius))
				{
					throw std::invalid_argument("Centreline bend radius must exceed the active outer profile radius.");
				}
				gp_Vec radial(normal);
				gp_Trsf plane_rotation;
				plane_rotation.SetRotation(gp_Ax1(origin, tangent), specification.rotation_radians);
				radial.Transform(plane_rotation);
				gp_Pnt center = origin.Translated(radial * specification.radius);
				gp_Vec start_radius(center, origin);
				gp_Dir axis = gp_Dir(start_radius).Crossed(tangent);
				gp_Trsf sweep;
				sweep.SetRotation(gp_Ax1(center, axis), specification.sweep_radians);
				gp_Pnt exit = origin.Transformed(sweep);
				gp_Dir exit_tangent = tangent.Transformed(sweep);
				gp_Dir exit_normal = normal.Transformed(sweep);
				feature.center = point(center);
				feature.radius = specification.radius;
				feature.sweep_radians = specification.sweep_radians;
				feature.length = specification.radius * specification.sweep_radians;
				frame.origin = point(exit);
				frame.tangent = direction(exit_tangent);
				frame.normal = direction(exit_normal);
			}
			else if (specification.kind == FGCAD_FEATURE_LOFT_TRANSITION)
			{
				if (!(specification.length > 0) || !std::isfinite(specification.length)
					|| !std::isfinite(specification.rotation_radians))
				{
					throw std::invalid_argument("Loft length and rotation must be finite and valid.");
				}
				gp_Trsf rotation;
				rotation.SetRotation(gp_Ax1(origin, tangent), specification.rotation_radians);
				frame.origin = point(origin.Translated(gp_Vec(tangent) * specification.length));
				frame.normal = direction(normal.Transformed(rotation));
				profile = specification.output_profile;
				feature.output_profile = profile;
				feature.length = specification.length;
				transported_span.reset();
			}
			else if (specification.kind == FGCAD_FEATURE_CUBIC_BEZIER)
			{
				if (!(specification.start_handle_length > 0)
					|| !std::isfinite(specification.start_handle_length))
				{
					throw std::invalid_argument("Cubic Bezier start handle must be positive and finite.");
				}
				gp_Vec t(tangent);
				gp_Vec u(normal);
				gp_Vec v = t.Crossed(u);
				auto local_point = [&](const fgcad_point3& local)
				{
					return origin.Translated(t * local.x + u * local.y + v * local.z);
				};
				gp_Pnt control1 = origin.Translated(t * specification.start_handle_length);
				gp_Pnt control2;
				gp_Pnt end;
				if (specification.has_constrained_end_frame != 0)
				{
					if (!(specification.end_handle_length > 0)
						|| !std::isfinite(specification.end_handle_length))
					{
						throw std::invalid_argument(
							"Constrained cubic Bezier end handle must be positive and finite.");
					}
					end = point(specification.constrained_end_frame.origin);
					gp_Dir end_tangent = unit(specification.constrained_end_frame.tangent);
					control2 = end.Translated(-gp_Vec(end_tangent) * specification.end_handle_length);
				}
				else
				{
					control2 = local_point(specification.control2_local);
					end = local_point(specification.end_local);
				}
				double outer_radius = profile.kind == FGCAD_PROFILE_CIRCULAR
					? profile.outer_diameter * 0.5
					: profile.equivalent_radius + profile.wall_thickness;
				fgcad_point3 control1_value = point(control1);
				fgcad_point3 control2_value = point(control2);
				fgcad_point3 end_value = point(end);
				fgcad_bezier_evaluation evaluation = evaluate_cubic_bezier_internal(
					frame, control1_value, control2_value, end_value, outer_radius);
				feature.control1 = control1_value;
				feature.control2 = control2_value;
				feature.length = evaluation.length;
				feature.radius = evaluation.minimum_radius;
				frame = evaluation.exit_frame;
			}
			else if (specification.kind == FGCAD_FEATURE_CLOCKING_TRANSITION)
			{
				if (!(specification.length > 0) || !std::isfinite(specification.length))
				{
					throw std::invalid_argument("Clocking-transition length must be positive and finite.");
				}
				double rotation = specification.rotation_radians;
				gp_Pnt exit = origin.Translated(gp_Vec(tangent) * specification.length);
				if (specification.has_constrained_end_frame != 0)
				{
					gp_Pnt target = point(specification.constrained_end_frame.origin);
					gp_Vec displacement(origin, target);
					double axial = displacement.Dot(gp_Vec(tangent));
					gp_Vec lateral = displacement - gp_Vec(tangent) * axial;
					if (std::abs(axial - specification.length) > Precision::Confusion()
						|| lateral.Magnitude() > Precision::Confusion())
					{
						throw std::invalid_argument(
							"Clocking-transition target must lie on its incoming axis.");
					}
					gp_Dir target_tangent = unit(specification.constrained_end_frame.tangent);
					if (gp_Vec(tangent).Dot(gp_Vec(target_tangent)) < 1.0 - Precision::Angular())
					{
						throw std::invalid_argument(
							"Clocking-transition target tangent must match its incoming tangent.");
					}
					gp_Dir target_normal = unit(specification.constrained_end_frame.normal);
					rotation = std::atan2(
						gp_Vec(tangent).Dot(gp_Vec(normal).Crossed(gp_Vec(target_normal))),
						gp_Vec(normal).Dot(gp_Vec(target_normal))
					);
					exit = target;
				}
				gp_Trsf roll;
				roll.SetRotation(gp_Ax1(origin, tangent), rotation);
				frame.origin = point(exit);
				frame.normal = direction(normal.Transformed(roll));
				feature.rotation_radians = rotation;
				feature.length = specification.length;
				transported_span.reset();
			}
			else
			{
				throw std::invalid_argument("Unknown runner feature specification kind.");
			}

			if (transported_span && !transition)
			{
				TopoDS_Edge edge;
				if (specification.kind == FGCAD_FEATURE_STRAIGHT)
				{
					edge = BRepBuilderAPI_MakeEdge(
						point(feature.entry_frame.origin),
						point(frame.origin)
					).Edge();
				}
				else if (specification.kind == FGCAD_FEATURE_BEND)
				{
					gp_Pnt start = point(feature.entry_frame.origin);
					gp_Pnt center = point(feature.center);
					gp_Vec start_radius(center, start);
					gp_Dir axis = gp_Dir(start_radius).Crossed(unit(feature.entry_frame.tangent));
					gp_Trsf half_rotation;
					half_rotation.SetRotation(
						gp_Ax1(center, axis),
						feature.sweep_radians * 0.5
					);
					Handle(Geom_TrimmedCurve) arc = GC_MakeArcOfCircle(
						start,
						start.Transformed(half_rotation),
						point(frame.origin)
					);
					edge = BRepBuilderAPI_MakeEdge(arc).Edge();
				}
				else
				{
					edge = BRepBuilderAPI_MakeEdge(make_bezier(
						feature.entry_frame,
						feature.control1,
						feature.control2,
						frame.origin
					)).Edge();
				}
				transported_span->Add(edge);
				if (!transported_span->IsDone())
				{
					throw std::runtime_error("The transported runner span could not form a G1 wire.");
				}
				frame = transport_frame(
					transported_span->Wire(),
					transported_span_entry,
					point(frame.origin),
					unit(frame.tangent)
				);
			}

			feature.exit_frame = frame;
			evaluated_features[index] = feature;
			*evaluated_count = index + 1;
		}
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_create(fgcad_document** document)
{
	return guarded([&]()
	{
		if (document == nullptr)
		{
			throw std::invalid_argument("The document output pointer cannot be null.");
		}

		*document = new fgcad_document();
		return FGCAD_STATUS_OK;
	});
}

void fgcad_document_destroy(fgcad_document* document)
{
	delete document;
}

fgcad_status fgcad_document_import_step(
	fgcad_document* document,
	const char* part_id,
	const char* path_utf8,
	const char* name_utf8
)
{
	return guarded([&]()
	{
		if (document == nullptr)
		{
			throw std::invalid_argument("The document cannot be null.");
		}

		part_record part;
		part.id = require_text(part_id, "part_id");
		part.name = require_text(name_utf8, "name_utf8");
		import_step(part, require_text(path_utf8, "path_utf8"));
		rebuild_topology(part);
		document->parts[part.id] = std::move(part);
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_replace_step(
	fgcad_document* document,
	const char* part_id,
	const char* path_utf8,
	const char* name_utf8
)
{
	return not_found_guarded([&]()
	{
		part_record& current = find_part(*document, require_text(part_id, "part_id"));
		part_record replacement;
		replacement.id = current.id;
		replacement.name = require_text(name_utf8, "name_utf8");
		replacement.placement = current.placement;
		import_step(replacement, require_text(path_utf8, "path_utf8"));
		rebuild_topology(replacement);
		current = std::move(replacement);

		for (auto selector = document->selectors.begin(); selector != document->selectors.end();)
		{
			if (selector->second.part_id == current.id)
			{
				selector = document->selectors.erase(selector);
			}
			else
			{
				++selector;
			}
		}

		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_set_part_transform(
	fgcad_document* document,
	const char* part_id,
	const fgcad_transform* value
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || value == nullptr)
		{
			throw std::invalid_argument("The document and transform cannot be null.");
		}

		find_part(*document, require_text(part_id, "part_id")).placement = transform(*value);
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_get_topology_count(
	fgcad_document* document,
	const char* part_id,
	size_t* count
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || count == nullptr)
		{
			throw std::invalid_argument("The document and count cannot be null.");
		}

		*count = find_part(*document, require_text(part_id, "part_id")).topology.size();
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_copy_topology(
	fgcad_document* document,
	const char* part_id,
	fgcad_topology_info* items,
	size_t capacity
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr)
		{
			throw std::invalid_argument("The document cannot be null.");
		}

		const part_record& part = find_part(*document, require_text(part_id, "part_id"));

		if (capacity < part.topology.size() || (items == nullptr && !part.topology.empty()))
		{
			throw std::invalid_argument("The topology destination buffer is too small.");
		}

		for (size_t index = 0; index < part.topology.size(); ++index)
		{
			items[index] = part.topology[index].info;
		}

		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_get_mate_frame(
	fgcad_document* document,
	const char* part_id,
	uint64_t topology_id,
	const fgcad_point3* local_hit,
	fgcad_frame* frame,
	double* radius
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || local_hit == nullptr || frame == nullptr || radius == nullptr)
		{
			throw std::invalid_argument("Mate frame arguments cannot be null.");
		}

		part_record& part = find_part(*document, require_text(part_id, "part_id"));
		auto found = std::find_if(part.topology.begin(), part.topology.end(), [&](const topology_record& item)
		{
			return item.info.id == topology_id;
		});

		if (found == part.topology.end())
		{
			throw std::out_of_range("The requested topology selection was not found.");
		}

		gp_Circ circle;

		if (found->info.kind == FGCAD_TOPOLOGY_CIRCULAR_EDGE)
		{
			circle = BRepAdaptor_Curve(TopoDS::Edge(found->shape)).Circle();
		}
		else if (found->info.kind == FGCAD_TOPOLOGY_CYLINDRICAL_FACE)
		{
			gp_Pnt hit = point(*local_hit).Transformed(part.placement.Inverted());
			double nearest = std::numeric_limits<double>::infinity();
			bool has_circle = false;

			for (TopExp_Explorer explorer(found->shape, TopAbs_EDGE); explorer.More(); explorer.Next())
			{
				TopoDS_Edge edge = TopoDS::Edge(explorer.Current());
				BRepAdaptor_Curve curve(edge);

				if (curve.GetType() != GeomAbs_Circle)
				{
					continue;
				}

				gp_Circ candidate = curve.Circle();
				double distance = hit.SquareDistance(candidate.Location());

				if (distance < nearest)
				{
					nearest = distance;
					circle = candidate;
					has_circle = true;
				}
			}

			if (!has_circle)
			{
				last_error = "The cylindrical face has no usable circular boundary.";
				return FGCAD_STATUS_UNSUPPORTED_TOPOLOGY;
			}
		}
		else if (found->info.kind == FGCAD_TOPOLOGY_CLOSED_PROFILE)
		{
			gp_Pnt origin = point(found->info.center);
			gp_Dir tangent = unit(found->info.axis);
			gp_Ax2 axes(origin, tangent);
			frame->origin = found->info.center;
			frame->tangent = found->info.axis;
			frame->normal = direction(axes.XDirection());
			*radius = found->info.radius;
			return FGCAD_STATUS_OK;
		}
		else
		{
			last_error = "Mate creation requires a circular edge, cylindrical face, or planar closed profile.";
			return FGCAD_STATUS_UNSUPPORTED_TOPOLOGY;
		}

		frame->origin = point(circle.Location());
		frame->tangent = direction(circle.Axis().Direction());
		frame->normal = direction(circle.XAxis().Direction());
		*radius = circle.Radius();
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_bind_topology_selector(
	fgcad_document* document,
	const char* selector_id,
	const char* part_id,
	uint64_t topology_id
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr)
		{
			throw std::invalid_argument("The document cannot be null.");
		}

		std::string part_key = require_text(part_id, "part_id");
		part_record& part = find_part(*document, part_key);

		if (std::none_of(part.topology.begin(), part.topology.end(), [&](const topology_record& item)
		{
			return item.info.id == topology_id;
		}))
		{
			throw std::out_of_range("The topology selector target was not found.");
		}

		selector_record selector;
		selector.id = require_text(selector_id, "selector_id");
		selector.part_id = part_key;
		selector.topology_id = topology_id;
		document->selectors[selector.id] = std::move(selector);
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_build_runner(
	fgcad_document* document,
	const char* runner_id,
	const char* runner_name,
	const fgcad_runner_feature* features,
	size_t feature_count
)
{
	return guarded([&]()
	{
		if (document == nullptr || features == nullptr || feature_count == 0)
		{
			throw std::invalid_argument("Runner features cannot be empty.");
		}

		const bool trace_timing = std::getenv("FGCAD_TRACE_TIMING") != nullptr;
		auto trace_last = std::chrono::steady_clock::now();
		auto trace = [&](const char* label)
		{
			if (!trace_timing) return;
			auto now = std::chrono::steady_clock::now();
			std::cerr << "[FGCAD] " << label << "="
				<< std::chrono::duration_cast<std::chrono::milliseconds>(now - trace_last).count()
				<< " ms\n";
			trace_last = now;
		};

		struct profile_wires
		{
			TopoDS_Wire inner;
			TopoDS_Wire outer;
		};

		auto frame_axes = [](const fgcad_frame& frame)
		{
			return gp_Ax2(point(frame.origin), unit(frame.tangent), unit(frame.normal));
		};

		auto wire_area = [](const TopoDS_Wire& wire)
		{
			BRepBuilderAPI_MakeFace face(wire, true);
			if (!face.IsDone() || !BRepCheck_Analyzer(face.Face(), true).IsValid()) return 0.0;
			GProp_GProps properties;
			BRepGProp::SurfaceProperties(face.Face(), properties);
			return std::abs(properties.Mass());
		};

		auto normalize_wire = [](const TopoDS_Wire& wire, const fgcad_frame& frame)
		{
			gp_Vec u(frame.normal.x, frame.normal.y, frame.normal.z);
			gp_Vec tangent(frame.tangent.x, frame.tangent.y, frame.tangent.z);
			gp_Vec v = tangent.Crossed(u);
			gp_Pnt origin = point(frame.origin);
			std::vector<std::pair<double, double>> samples;
			for (BRepTools_WireExplorer explorer(wire); explorer.More(); explorer.Next())
			{
				BRepAdaptor_Curve curve(explorer.Current());
				double first = curve.FirstParameter();
				double last = curve.LastParameter();
				int steps = curve.GetType() == GeomAbs_Line ? 1 : 12;
				for (int index = 0; index < steps; ++index)
				{
					double parameter = first + (last - first) * static_cast<double>(index) / steps;
					gp_Vec relative(origin, curve.Value(parameter));
				samples.emplace_back(relative.Dot(u), relative.Dot(v));
				}
			}
			if (samples.size() < 3) return wire;
			double signed_area = 0;
			for (size_t index = 0; index < samples.size(); ++index)
			{
				const auto& current = samples[index];
				const auto& next = samples[(index + 1) % samples.size()];
				signed_area += current.first * next.second - next.first * current.second;
			}
			return signed_area < 0 ? TopoDS::Wire(wire.Reversed()) : wire;
		};

		auto outward_offset = [&](const TopoDS_Wire& inner, double wall)
		{
			if (!(wall > 0) || !std::isfinite(wall))
			{
				throw std::invalid_argument("Profile wall thickness must be positive and finite.");
			}

			double inner_area = wire_area(inner);
			TopoDS_Wire best;
			double best_area = inner_area;
			for (double sign : { 1.0, -1.0 })
			{
				BRepOffsetAPI_MakeOffset offset(inner, GeomAbs_Arc, false);
				offset.Perform(sign * wall);
				if (!offset.IsDone()) continue;
				std::vector<TopoDS_Wire> candidates;
				for (TopExp_Explorer explorer(offset.Shape(), TopAbs_WIRE); explorer.More(); explorer.Next())
				{
					candidates.push_back(TopoDS::Wire(explorer.Current()));
				}
				if (candidates.size() != 1) continue;
				double area = wire_area(candidates[0]);
				if (area > best_area)
				{
					best_area = area;
					best = candidates[0];
				}
			}
			if (best.IsNull())
			{
				throw std::runtime_error("The mate profile could not produce one valid outward wall offset.");
			}
			return best;
		};

		auto mate_wire = [&](const fgcad_runner_profile& profile, const fgcad_frame& frame)
		{
			std::string mate_id_value(profile.mate_id);
			auto selector = document->selectors.find(mate_id_value);
			if (selector == document->selectors.end())
			{
				throw std::out_of_range("The runner's exact mate-profile selector was not found.");
			}
			part_record& part = find_part(*document, selector->second.part_id);
			auto topology = std::find_if(part.topology.begin(), part.topology.end(), [&](const topology_record& item)
			{
				return item.info.id == selector->second.topology_id;
			});
			if (topology == part.topology.end())
			{
				throw std::out_of_range("The exact mate-profile topology was not found.");
			}

			TopoDS_Wire result;
			if (topology->shape.ShapeType() == TopAbs_WIRE)
			{
				result = TopoDS::Wire(topology->shape);
			}
			else if (topology->shape.ShapeType() == TopAbs_EDGE)
			{
				BRepBuilderAPI_MakeWire builder(TopoDS::Edge(topology->shape));
				result = builder.Wire();
			}
			else if (topology->shape.ShapeType() == TopAbs_FACE)
			{
				double nearest = std::numeric_limits<double>::infinity();
				gp_Pnt target = point(frame.origin).Transformed(part.placement.Inverted());
				for (TopExp_Explorer explorer(topology->shape, TopAbs_EDGE); explorer.More(); explorer.Next())
				{
					TopoDS_Edge edge = TopoDS::Edge(explorer.Current());
					BRepAdaptor_Curve curve(edge);
					if (curve.GetType() != GeomAbs_Circle) continue;
					double distance = target.SquareDistance(curve.Circle().Location());
					if (distance < nearest)
					{
						nearest = distance;
						result = BRepBuilderAPI_MakeWire(edge).Wire();
					}
				}
			}
			if (result.IsNull())
			{
				throw std::runtime_error("The selected mate topology has no usable closed profile wire.");
			}
			fgcad_frame source_frame{};
			source_frame.origin = topology->info.center;
			source_frame.tangent = topology->info.axis;
			if (topology->info.kind == FGCAD_TOPOLOGY_CIRCULAR_EDGE)
			{
				source_frame.normal = direction(
					BRepAdaptor_Curve(TopoDS::Edge(topology->shape)).Circle().XAxis().Direction());
			}
			else
			{
				gp_Ax2 axes(
					point(source_frame.origin),
					unit(source_frame.tangent)
				);
				source_frame.normal = direction(axes.XDirection());
			}
			gp_Pnt source_origin = point(source_frame.origin).Transformed(part.placement);
			gp_Dir source_tangent = unit(source_frame.tangent).Transformed(part.placement);
			gp_Dir source_normal = unit(source_frame.normal).Transformed(part.placement);
			gp_Ax3 from(source_origin, source_tangent, source_normal);
			gp_Ax3 to(point(frame.origin), unit(frame.tangent), unit(frame.normal));
			gp_Trsf displacement;
			displacement.SetDisplacement(from, to);
			TopoDS_Shape placed_wire = result.Moved(TopLoc_Location(part.placement));
			return TopoDS::Wire(placed_wire.Moved(TopLoc_Location(displacement)));
		};

		auto profile_at = [&](const fgcad_runner_profile& profile, const fgcad_frame& frame)
		{
			profile_wires result;
			if (profile.kind == FGCAD_PROFILE_CIRCULAR)
			{
				double outer_radius = profile.outer_diameter * 0.5;
				double inner_radius = outer_radius - profile.wall_thickness;
				if (!(outer_radius > 0) || !(profile.wall_thickness > 0) || !(inner_radius > 0))
				{
					throw std::invalid_argument("The circular pipe profile is invalid.");
				}
				gp_Ax2 axes = frame_axes(frame);
				result.outer = BRepBuilderAPI_MakeWire(BRepBuilderAPI_MakeEdge(gp_Circ(axes, outer_radius))).Wire();
				result.inner = BRepBuilderAPI_MakeWire(BRepBuilderAPI_MakeEdge(gp_Circ(axes, inner_radius))).Wire();
			}
			else if (profile.kind == FGCAD_PROFILE_MATE)
			{
				result.inner = mate_wire(profile, frame);
				result.outer = outward_offset(result.inner, profile.wall_thickness);
			}
			else
			{
				throw std::invalid_argument("Unknown runner profile kind.");
			}
			result.inner = normalize_wire(result.inner, frame);
			result.outer = normalize_wire(result.outer, frame);
			return result;
		};

		auto annular_face = [](const profile_wires& profile)
		{
			BRepBuilderAPI_MakeFace builder(profile.outer);
			builder.Add(TopoDS::Wire(profile.inner.Reversed()));
			if (!builder.IsDone()) throw std::runtime_error("A hollow runner profile face could not be built.");
			return builder.Face();
		};

		auto feature_edge = [](const fgcad_runner_feature& feature) -> TopoDS_Edge
		{
			if (feature.kind == FGCAD_FEATURE_STRAIGHT)
			{
				return BRepBuilderAPI_MakeEdge(point(feature.entry_frame.origin), point(feature.exit_frame.origin)).Edge();
			}
			if (feature.kind == FGCAD_FEATURE_BEND)
			{
				gp_Pnt start = point(feature.entry_frame.origin);
				gp_Pnt center = point(feature.center);
				gp_Vec radius(center, start);
				gp_Dir axis = gp_Dir(radius).Crossed(unit(feature.entry_frame.tangent));
				gp_Trsf half_rotation;
				half_rotation.SetRotation(gp_Ax1(center, axis), feature.sweep_radians * 0.5);
				gp_Pnt middle = start.Transformed(half_rotation);
				Handle(Geom_TrimmedCurve) arc = GC_MakeArcOfCircle(
					start, middle, point(feature.exit_frame.origin));
				return BRepBuilderAPI_MakeEdge(arc).Edge();
			}
			if (feature.kind == FGCAD_FEATURE_CUBIC_BEZIER)
			{
				fgcad_bezier_evaluation evaluation = evaluate_cubic_bezier_internal(
					feature.entry_frame,
					feature.control1,
					feature.control2,
					feature.exit_frame.origin,
					feature.input_profile.kind == FGCAD_PROFILE_CIRCULAR
						? feature.input_profile.outer_diameter * 0.5
						: feature.input_profile.equivalent_radius + feature.input_profile.wall_thickness
				);
				(void)evaluation;
				return BRepBuilderAPI_MakeEdge(make_bezier(
					feature.entry_frame,
					feature.control1,
					feature.control2,
					feature.exit_frame.origin
				)).Edge();
			}
			throw std::invalid_argument("A profile transition does not have a sweep edge.");
		};

		struct generated_section
		{
			TopoDS_Shape shape;
			std::vector<runner_source> sources;
		};

		auto append_faces = [](const TopoDS_Shape& shape, std::vector<TopoDS_Face>& faces)
		{
			if (shape.ShapeType() == TopAbs_FACE)
			{
				faces.push_back(TopoDS::Face(shape));
				return;
			}
			for (TopExp_Explorer explorer(shape, TopAbs_FACE); explorer.More(); explorer.Next())
			{
				faces.push_back(TopoDS::Face(explorer.Current()));
			}
		};

		auto same_profile = [](const fgcad_runner_profile& left, const fgcad_runner_profile& right)
		{
			return left.kind == right.kind
				&& std::strcmp(left.mate_id, right.mate_id) == 0
				&& left.outer_diameter == right.outer_diameter
				&& left.wall_thickness == right.wall_thickness;
		};

		auto make_sweep = [&](size_t first, size_t last)
		{
			BRepBuilderAPI_MakeWire wire;
			std::vector<TopoDS_Edge> edges;
			edges.reserve(last - first);
			const fgcad_runner_profile& profile = features[first].input_profile;
			bool contains_bezier = false;

			for (size_t index = first; index < last; ++index)
			{
				const fgcad_runner_feature& feature = features[index];
				if (feature.kind == FGCAD_FEATURE_LOFT_TRANSITION
					|| feature.kind == FGCAD_FEATURE_CLOCKING_TRANSITION
					|| !same_profile(profile, feature.input_profile)
					|| !same_profile(profile, feature.output_profile))
				{
					throw std::invalid_argument("A constant-profile sweep group contains incompatible features.");
				}
				contains_bezier = contains_bezier || feature.kind == FGCAD_FEATURE_CUBIC_BEZIER;
				edges.push_back(feature_edge(feature));
				wire.Add(edges.back());
			}

			if (!wire.IsDone()) throw std::runtime_error("The grouped runner spine could not be built.");
			TopoDS_Face section_face = annular_face(profile_at(profile, features[first].entry_frame));
			BRepOffsetAPI_MakePipe pipe = contains_bezier
				? BRepOffsetAPI_MakePipe(
					wire.Wire(),
					section_face,
					GeomFill_IsDiscreteTrihedron,
					true)
				: BRepOffsetAPI_MakePipe(wire.Wire(), section_face);
			if (!pipe.IsDone()) throw std::runtime_error("Open CASCADE could not sweep a grouped runner spine.");
			trace("grouped sweep");

			generated_section section;
			section.shape = pipe.Shape();
			for (size_t index = first; index < last; ++index)
			{
				runner_source source;
				source.id = features[index].source_node_id;
				source.feature = features[index];
				const NCollection_List<TopoDS_Shape>& generated = pipe.Generated(edges[index - first]);
				for (NCollection_List<TopoDS_Shape>::Iterator iterator(generated); iterator.More(); iterator.Next())
				{
					append_faces(iterator.Value(), source.faces);
				}
				const NCollection_List<TopoDS_Shape>& modified = pipe.Modified(edges[index - first]);
				for (NCollection_List<TopoDS_Shape>::Iterator iterator(modified); iterator.More(); iterator.Next())
				{
					append_faces(iterator.Value(), source.faces);
				}
				section.sources.push_back(std::move(source));
			}
			std::vector<TopoDS_Face> sweep_faces = shape_faces(section.shape);
			auto face_is_claimed = [&](const TopoDS_Face& face)
			{
				return std::any_of(
					section.sources.begin(),
					section.sources.end(),
					[&](const runner_source& source)
					{
						return std::any_of(
							source.faces.begin(),
							source.faces.end(),
							[&](const TopoDS_Face& source_face)
							{
								return source_face.IsSame(face);
							});
					});
			};
			auto distance_to_spine_edge = [&](const gp_Pnt& value, size_t edge_index)
			{
				double edge_first = 0;
				double edge_last = 0;
				Handle(Geom_Curve) curve = BRep_Tool::Curve(
					edges[edge_index],
					edge_first,
					edge_last);
				if (curve.IsNull())
				{
					return std::numeric_limits<double>::infinity();
				}
				GeomAPI_ProjectPointOnCurve projection(
					value,
					curve,
					edge_first,
					edge_last);
				return projection.NbPoints() > 0
					? projection.LowerDistance() * projection.LowerDistance()
					: std::numeric_limits<double>::infinity();
			};
			auto append_face = [](runner_source& source, const TopoDS_Face& face)
			{
				if (std::none_of(
					source.faces.begin(),
					source.faces.end(),
					[&](const TopoDS_Face& existing)
					{
						return existing.IsSame(face);
					}))
				{
					source.faces.push_back(face);
				}
			};

			for (const TopoDS_Face& face : sweep_faces)
			{
				if (face_is_claimed(face))
				{
					continue;
				}
				GProp_GProps properties;
				BRepGProp::SurfaceProperties(face, properties);
				gp_Pnt center = properties.CentreOfMass();
				std::vector<double> distances;
				distances.reserve(edges.size());
				double best = std::numeric_limits<double>::infinity();
				for (size_t edge_index = 0; edge_index < edges.size(); ++edge_index)
				{
					double distance = distance_to_spine_edge(center, edge_index);
					distances.push_back(distance);
					best = std::min(best, distance);
				}
				double tie_tolerance = std::max(
					Precision::SquareConfusion(),
					best * 1.0e-9);
				for (size_t source_index = 0; source_index < section.sources.size(); ++source_index)
				{
					if (distances[source_index] <= best + tie_tolerance)
					{
						append_face(section.sources[source_index], face);
					}
				}
			}

			for (size_t source_index = 0; source_index < section.sources.size(); ++source_index)
			{
				if (!section.sources[source_index].faces.empty())
				{
					continue;
				}
				double best = std::numeric_limits<double>::infinity();
				const TopoDS_Face* nearest = nullptr;
				for (const TopoDS_Face& face : sweep_faces)
				{
					GProp_GProps properties;
					BRepGProp::SurfaceProperties(face, properties);
					double distance = distance_to_spine_edge(
						properties.CentreOfMass(),
						source_index);
					if (distance < best)
					{
						best = distance;
						nearest = &face;
					}
				}
				if (nearest != nullptr)
				{
					append_face(section.sources[source_index], *nearest);
				}
			}
			return section;
		};

		auto make_loft = [&](const fgcad_runner_feature& feature)
		{
			profile_wires input = profile_at(feature.input_profile, feature.entry_frame);
			profile_wires output = profile_at(feature.output_profile, feature.exit_frame);
			trace("loft profiles");
			BRepOffsetAPI_ThruSections outer(true, false);
			outer.CheckCompatibility(true);
			outer.AddWire(input.outer);
			outer.AddWire(output.outer);
			outer.Build();
			BRepOffsetAPI_ThruSections inner(true, false);
			inner.CheckCompatibility(true);
			inner.AddWire(input.inner);
			inner.AddWire(output.inner);
			inner.Build();
			if (!outer.IsDone() || !inner.IsDone())
			{
				throw std::runtime_error(
					"Open CASCADE could not loft the profile-transition volumes.");
			}
			trace("loft volumes");
			BRepAlgoAPI_Cut hollow(outer.Shape(), inner.Shape());
			hollow.Build();
			if (!hollow.IsDone() || hollow.Shape().IsNull())
			{
				throw std::runtime_error(
					"The inner profile-transition volume could not be subtracted.");
			}
			trace("loft hollow cut");
			return hollow.Shape();
		};

		runner_record replacement;
		replacement.id = require_text(runner_id, "runner_id");
		replacement.name = require_text(runner_name, "runner_name");
		TopoDS_Shape result;
		auto is_joint_cap = [](const TopoDS_Face& face, const fgcad_frame& frame)
		{
			BRepAdaptor_Surface surface(face, true);
			if (surface.GetType() != GeomAbs_Plane) return false;
			gp_Pln plane = surface.Plane();
			double distance = plane.Distance(point(frame.origin));
			double alignment = std::abs(plane.Axis().Direction().Dot(unit(frame.tangent)));
			return distance <= 1.0e-6 && alignment >= 1.0 - 1.0e-9;
		};
		auto try_sew_join = [&](const TopoDS_Shape& left, const TopoDS_Shape& right,
			const fgcad_frame& frame, TopoDS_Shape& joined)
		{
			BRepBuilderAPI_Sewing sewing;
			size_t removed_caps = 0;
			auto add_without_joint_cap = [&](const TopoDS_Shape& shape)
			{
				for (TopExp_Explorer explorer(shape, TopAbs_FACE); explorer.More(); explorer.Next())
				{
					TopoDS_Face face = TopoDS::Face(explorer.Current());
					if (is_joint_cap(face, frame))
					{
						++removed_caps;
						continue;
					}
					sewing.Add(face);
				}
			};
			add_without_joint_cap(left);
			add_without_joint_cap(right);
			if (removed_caps != 2) return false;

			sewing.Perform();
			TopoDS_Shape sewed = sewing.SewedShape();
			if (sewed.IsNull() || sewed.ShapeType() != TopAbs_SHELL) return false;
			BRepBuilderAPI_MakeSolid solid(TopoDS::Shell(sewed));
			if (!solid.IsDone()) return false;
			TopoDS_Shape candidate = solid.Solid();
			if (!BRepCheck_Analyzer(candidate, true).IsValid()) return false;

			for (runner_source& source : replacement.sources)
			{
				for (TopoDS_Face& face : source.faces)
				{
					if (sewing.IsModifiedSubShape(face))
					{
						TopoDS_Shape modified = sewing.ModifiedSubShape(face);
						if (!modified.IsNull() && modified.ShapeType() == TopAbs_FACE)
						{
							face = TopoDS::Face(modified);
						}
					}
				}
			}
			joined = candidate;
			return true;
		};
		auto join_section = [&](generated_section&& section, const fgcad_frame* joint_frame)
		{
			if (section.shape.IsNull() || !BRepCheck_Analyzer(section.shape, true).IsValid())
			{
				throw std::runtime_error("A generated runner feature failed exact B-rep validation.");
			}
			trace("section validation");
			for (runner_source& source : section.sources)
			{
				replacement.sources.push_back(std::move(source));
			}

			if (result.IsNull()) result = section.shape;
			else
			{
				TopoDS_Shape sewn;
				if (joint_frame != nullptr && try_sew_join(result, section.shape, *joint_frame, sewn))
				{
					result = sewn;
					trace("section sew");
					return;
				}
				BRepAlgoAPI_Fuse fuse(result, section.shape);
				fuse.Build();
				if (!fuse.IsDone()) throw std::runtime_error("Adjacent runner features could not be joined.");
				trace("section fuse");
				apply_boolean_history(fuse, replacement.sources);
				result = fuse.Shape();
				if (result.IsNull() || !BRepCheck_Analyzer(result, true).IsValid())
				{
					throw std::runtime_error("An intermediate runner join failed exact B-rep validation.");
				}
				trace("joined validation");
			}
		};

		for (size_t index = 0; index < feature_count;)
		{
			if (features[index].kind == FGCAD_FEATURE_LOFT_TRANSITION
				|| features[index].kind == FGCAD_FEATURE_CLOCKING_TRANSITION)
			{
				generated_section section;
				section.shape = make_loft(features[index]);
				section.sources.push_back({
					features[index].source_node_id,
					features[index],
					shape_faces(section.shape)
				});
				join_section(std::move(section), &features[index].entry_frame);
				++index;
				continue;
			}

			size_t end = index + 1;
			while (end < feature_count
				&& features[end].kind != FGCAD_FEATURE_LOFT_TRANSITION
				&& features[end].kind != FGCAD_FEATURE_CLOCKING_TRANSITION
				&& same_profile(features[index].input_profile, features[end].input_profile))
			{
				++end;
			}
			join_section(make_sweep(index, end), &features[index].entry_frame);
			index = end;
		}

		if (result.IsNull() || !BRepCheck_Analyzer(result, true).IsValid())
		{
			throw std::runtime_error("The complete runner failed exact B-rep validation.");
		}
		trace("final validation");

		replacement.shape = result;
		for (runner_source& source : replacement.sources)
		{
			source.kind = FGCAD_SOURCE_RUNNER_NODE;
			source.owner_id = replacement.id;
		}
		if (!document->staged_collector_id.empty())
		{
			document->staged_runners[replacement.id] = std::move(replacement);
		}
		else
		{
			document->runners[replacement.id] = std::move(replacement);
		}
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_remove_runner(fgcad_document* document, const char* runner_id)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(runner_id, "runner_id");
		document->runners.erase(id);
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_rename_runner(
	fgcad_document* document,
	const char* runner_id,
	const char* runner_name
)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(runner_id, "runner_id");
		std::string name = require_text(runner_name, "runner_name");
		auto found = document->runners.find(id);
		if (found != document->runners.end())
		{
			found->second.name = std::move(name);
		}
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_build_collector_system(
	fgcad_document* document,
	const fgcad_collector_system_spec* system,
	const fgcad_collector_inlet* inlets,
	size_t inlet_count
)
{
	return guarded([&]()
	{
		if (document == nullptr || system == nullptr || inlets == nullptr || inlet_count < 2)
		{
			throw std::invalid_argument(
				"A collector system requires a document, system specification, and at least two inlets.");
		}
		if (!(system->outlet_stub_length > 0) || !(system->merge_length > 0)
			|| !(system->overlap_length > 0) || !(system->branch_end_handle_length > 0))
		{
			throw std::invalid_argument("Collector lengths and handles must be positive.");
		}
		if (system->outlet_profile.kind != FGCAD_PROFILE_CIRCULAR)
		{
			throw std::invalid_argument("The collector outlet profile must be circular.");
		}

		auto radii = [](const fgcad_runner_profile& profile)
		{
			double outer = profile.kind == FGCAD_PROFILE_CIRCULAR
				? profile.outer_diameter * 0.5
				: profile.equivalent_radius + profile.wall_thickness;
			double inner = profile.kind == FGCAD_PROFILE_CIRCULAR
				? outer - profile.wall_thickness
				: profile.equivalent_radius;
			if (!(outer > 0) || !(inner > 0) || !(profile.wall_thickness > 0))
			{
				throw std::invalid_argument("A collector circular profile is invalid.");
			}
			return std::pair<double, double>(outer, inner);
		};
		auto disk = [](const fgcad_frame& frame, double radius)
		{
			gp_Ax2 axes(point(frame.origin), unit(frame.tangent), unit(frame.normal));
			TopoDS_Wire wire = BRepBuilderAPI_MakeWire(
				BRepBuilderAPI_MakeEdge(gp_Circ(axes, radius))).Wire();
			BRepBuilderAPI_MakeFace face(wire, true);
			if (!face.IsDone()) throw std::runtime_error("A collector section disk could not be built.");
			return face.Face();
		};
		struct collector_profile_faces
		{
			TopoDS_Face outer;
			TopoDS_Face inner;
		};
		auto wire_area = [](const TopoDS_Wire& wire)
		{
			BRepBuilderAPI_MakeFace face(wire, true);
			if (!face.IsDone()) return 0.0;
			GProp_GProps properties;
			BRepGProp::SurfaceProperties(face.Face(), properties);
			return std::abs(properties.Mass());
		};
		auto outward_offset = [&](const TopoDS_Wire& inner, double wall)
		{
			double inner_area = wire_area(inner);
			TopoDS_Wire best;
			double best_area = inner_area;
			for (double sign : { 1.0, -1.0 })
			{
				BRepOffsetAPI_MakeOffset offset(inner, GeomAbs_Arc, false);
				offset.Perform(sign * wall);
				if (!offset.IsDone()) continue;
				std::vector<TopoDS_Wire> candidates;
				for (TopExp_Explorer explorer(offset.Shape(), TopAbs_WIRE);
					explorer.More(); explorer.Next())
				{
					candidates.push_back(TopoDS::Wire(explorer.Current()));
				}
				if (candidates.size() != 1) continue;
				double area = wire_area(candidates.front());
				if (area > best_area)
				{
					best_area = area;
					best = candidates.front();
				}
			}
			if (best.IsNull())
			{
				throw std::runtime_error(
					"An arbitrary collector inlet could not produce one enclosing wall offset.");
			}
			return best;
		};
		auto mate_wire_at = [&](const fgcad_runner_profile& profile,
			const fgcad_frame& reference,
			const fgcad_frame& target)
		{
			auto selector = document->selectors.find(std::string(profile.mate_id));
			if (selector == document->selectors.end())
			{
				throw std::out_of_range(
					"The arbitrary collector inlet profile selector was not found.");
			}
			part_record& part = find_part(*document, selector->second.part_id);
			auto topology = std::find_if(
				part.topology.begin(),
				part.topology.end(),
				[&](const topology_record& item)
				{
					return item.info.id == selector->second.topology_id;
				});
			if (topology == part.topology.end())
			{
				throw std::out_of_range(
					"The arbitrary collector inlet profile topology was not found.");
			}
			TopoDS_Wire wire;
			if (topology->shape.ShapeType() == TopAbs_WIRE)
			{
				wire = TopoDS::Wire(topology->shape);
			}
			else if (topology->shape.ShapeType() == TopAbs_EDGE)
			{
				wire = BRepBuilderAPI_MakeWire(TopoDS::Edge(topology->shape)).Wire();
			}
			else if (topology->shape.ShapeType() == TopAbs_FACE)
			{
				for (TopExp_Explorer explorer(topology->shape, TopAbs_WIRE);
					explorer.More(); explorer.Next())
				{
					TopoDS_Wire candidate = TopoDS::Wire(explorer.Current());
					if (wire.IsNull() || wire_area(candidate) > wire_area(wire))
					{
						wire = candidate;
					}
				}
			}
			if (wire.IsNull())
			{
				throw std::runtime_error(
					"The arbitrary collector inlet has no usable exact closed wire.");
			}
			wire = TopoDS::Wire(wire.Moved(TopLoc_Location(part.placement)));
			gp_Ax3 from(
				point(reference.origin),
				unit(reference.tangent),
				unit(reference.normal)
			);
			gp_Ax3 to(
				point(target.origin),
				unit(target.tangent),
				unit(target.normal)
			);
			gp_Trsf displacement;
			displacement.SetDisplacement(from, to);
			return TopoDS::Wire(wire.Moved(TopLoc_Location(displacement)));
		};
		auto profile_faces = [&](const fgcad_collector_inlet& inlet)
		{
			collector_profile_faces result;
			if (inlet.profile.kind == FGCAD_PROFILE_CIRCULAR)
			{
				auto profile_radii = radii(inlet.profile);
				result.outer = disk(inlet.frame, profile_radii.first);
				result.inner = disk(inlet.frame, profile_radii.second);
			}
			else
			{
				TopoDS_Wire inner = mate_wire_at(
					inlet.profile,
					inlet.profile_reference_frame,
					inlet.frame
				);
				TopoDS_Wire outer = outward_offset(inner, inlet.profile.wall_thickness);
				BRepBuilderAPI_MakeFace outer_face(outer, true);
				BRepBuilderAPI_MakeFace inner_face(inner, true);
				if (!outer_face.IsDone() || !inner_face.IsDone()
					|| wire_area(outer) <= wire_area(inner)
					|| !BRepCheck_Analyzer(outer_face.Face(), true).IsValid()
					|| !BRepCheck_Analyzer(inner_face.Face(), true).IsValid())
				{
					throw std::runtime_error(
						"The arbitrary collector inlet profile is invalid or collapsed.");
				}
				result.outer = outer_face.Face();
				result.inner = inner_face.Face();
			}
			return result;
		};
		auto swept_volume = [&](const fgcad_frame& frame,
			const gp_Pnt& control1,
			const gp_Pnt& control2,
			const gp_Pnt& end,
			const TopoDS_Face& section,
			double interface_overlap)
		{
			fgcad_point3 c1 = point(control1);
			fgcad_point3 c2 = point(control2);
			fgcad_point3 p3 = point(end);
			Handle(Geom_BezierCurve) curve = make_bezier(frame, c1, c2, p3);
			TopoDS_Edge edge = BRepBuilderAPI_MakeEdge(curve).Edge();
			gp_Vec lead = -gp_Vec(unit(frame.tangent)) * interface_overlap;
			gp_Pnt lead_start = point(frame.origin).Translated(lead);
			BRepBuilderAPI_MakeWire wire_builder;
			wire_builder.Add(BRepBuilderAPI_MakeEdge(lead_start, point(frame.origin)).Edge());
			wire_builder.Add(edge);
			if (!wire_builder.IsDone())
			{
				throw std::runtime_error("A collector branch lead-in could not be built.");
			}
			TopoDS_Wire wire = wire_builder.Wire();
			gp_Trsf section_translation;
			section_translation.SetTranslation(lead);
			TopoDS_Face lead_section = TopoDS::Face(
				section.Moved(TopLoc_Location(section_translation)));
			BRepOffsetAPI_MakePipe pipe(
				wire,
				lead_section,
				GeomFill_IsDiscreteTrihedron,
				true
			);
			if (!pipe.IsDone() || pipe.Shape().IsNull())
			{
				throw std::runtime_error("Open CASCADE could not sweep a collector branch volume.");
			}
			return pipe.Shape();
		};
		auto fuse_all = [](const std::vector<TopoDS_Shape>& values,
			bool glue,
			std::vector<runner_source>* sources)
		{
			if (values.empty()) throw std::invalid_argument("A collector fusion cannot be empty.");
			if (values.size() == 1) return values.front();
			NCollection_List<TopoDS_Shape> arguments;
			NCollection_List<TopoDS_Shape> tools;
			arguments.Append(values.front());
			for (size_t index = 1; index < values.size(); ++index)
			{
				tools.Append(values[index]);
			}
			BRepAlgoAPI_Fuse fuse;
			fuse.SetArguments(arguments);
			fuse.SetTools(tools);
			fuse.SetNonDestructive(true);
			fuse.SetRunParallel(true);
			if (glue) fuse.SetGlue(BOPAlgo_GlueFull);
			fuse.SetFuzzyValue(Precision::Confusion());
			fuse.Build();
			if (!fuse.IsDone() || fuse.Shape().IsNull())
			{
				throw std::runtime_error("Collector volume fusion failed.");
			}
			if (sources != nullptr)
			{
				apply_boolean_history(fuse, *sources);
			}
			return fuse.Shape();
		};
		auto solid_count = [](const TopoDS_Shape& shape)
		{
			size_t count = 0;
			for (TopExp_Explorer explorer(shape, TopAbs_SOLID); explorer.More(); explorer.Next()) ++count;
			return count;
		};
		auto positive_common_volume = [](const TopoDS_Shape& left,
			const TopoDS_Shape& right,
			double scale)
		{
			BRepAlgoAPI_Common common(left, right);
			common.SetRunParallel(true);
			common.Build();
			if (!common.IsDone() || common.Shape().IsNull()) return false;
			GProp_GProps properties;
			BRepGProp::VolumeProperties(common.Shape(), properties);
			double tolerance = std::max(
				std::pow(Precision::Confusion(), 3),
				std::pow(std::max(scale, 1.0), 3) * 1.0e-12
			);
			return std::abs(properties.Mass()) > tolerance;
		};
		auto section_inside_point = [](const TopoDS_Face& face)
		{
			double u_min = 0;
			double u_max = 0;
			double v_min = 0;
			double v_max = 0;
			BRepTools::UVBounds(face, u_min, u_max, v_min, v_max);
			BRepAdaptor_Surface surface(face);
			for (int row = 1; row < 16; ++row)
			{
				double v = v_min + (v_max - v_min) * row / 16.0;
				for (int column = 1; column < 16; ++column)
				{
					double u = u_min + (u_max - u_min) * column / 16.0;
					BRepClass_FaceClassifier classifier(
						face,
						gp_Pnt2d(u, v),
						Precision::Confusion()
					);
					if (classifier.State() == TopAbs_IN)
					{
						return surface.Value(u, v);
					}
				}
			}
			throw std::runtime_error(
				"A collector opening has no classifiable interior sample point.");
		};

		std::string system_id = require_text(system->system_id, "system_id");
		if (document->staged_collector_id.empty())
		{
			throw std::invalid_argument(
				"A collector system must be built inside an active staged generation.");
		}
		if (document->staged_collector_id != system_id
			|| document->staged_generation_revision != system->generation_revision)
		{
			throw std::invalid_argument(
				"The collector build does not match the active native staging generation.");
		}
		collector_record replacement;
		replacement.id = system_id;
		replacement.name = require_text(system->name, "name");
		replacement.generation_revision = system->generation_revision;
		if (!std::isfinite(system->outlet_stub_length)
			|| !std::isfinite(system->merge_length)
			|| !std::isfinite(system->overlap_length)
			|| !std::isfinite(system->branch_end_handle_length)
			|| !(system->outlet_stub_length > 0)
			|| !(system->merge_length > 0)
			|| !(system->overlap_length > 0)
			|| !(system->branch_end_handle_length > 0))
		{
			throw std::invalid_argument(
				"Collector stub, merge, overlap, and terminal-handle lengths must be finite and positive.");
		}

		auto outlet_radii = radii(system->outlet_profile);
		gp_Pnt outlet_origin = point(system->outlet_frame.origin);
		gp_Dir outlet_tangent = unit(system->outlet_frame.tangent);
		double trunk_length = system->outlet_stub_length
			+ system->merge_length
			+ system->overlap_length;
		gp_Pnt trunk_start = outlet_origin.Translated(-gp_Vec(outlet_tangent) * trunk_length);
		gp_Ax2 trunk_axes(trunk_start, outlet_tangent, unit(system->outlet_frame.normal));
		TopoDS_Shape outer_union = BRepPrimAPI_MakeCylinder(
			trunk_axes,
			outlet_radii.first,
			trunk_length
		).Shape();
		TopoDS_Shape inner_trunk = BRepPrimAPI_MakeCylinder(
			trunk_axes,
			outlet_radii.second,
			trunk_length
		).Shape();
		std::vector<TopoDS_Shape> outer_volumes{ outer_union };
		std::vector<TopoDS_Shape> inner_volumes{ inner_trunk };
		std::vector<gp_Pnt> flow_samples;
		flow_samples.push_back(outlet_origin.Translated(
			-gp_Vec(outlet_tangent)
				* std::max(Precision::Confusion() * 100.0, outlet_radii.second * 0.1)));
		runner_source trunk_source;
		trunk_source.id = "trunk";
		trunk_source.kind = FGCAD_SOURCE_COLLECTOR_TRUNK;
		trunk_source.owner_id = system_id;
		runner_source outlet_source;
		outlet_source.id = "outlet";
		outlet_source.kind = FGCAD_SOURCE_COLLECTOR_OUTLET;
		outlet_source.owner_id = system_id;
		outlet_source.feature.entry_frame = system->outlet_frame;
		outlet_source.feature.exit_frame = system->outlet_frame;
		auto append_trunk_face = [&](const TopoDS_Face& face)
		{
			bool is_outlet = false;
			BRepAdaptor_Surface surface(face);
			if (surface.GetType() == GeomAbs_Plane)
			{
				GProp_GProps properties;
				BRepGProp::SurfaceProperties(face, properties);
				gp_Vec from_outlet(outlet_origin, properties.CentreOfMass());
				is_outlet = std::abs(from_outlet.Dot(gp_Vec(outlet_tangent)))
					<= Precision::Confusion() * 100.0;
			}
			(is_outlet ? outlet_source.faces : trunk_source.faces).push_back(face);
		};
		for (const TopoDS_Face& face : shape_faces(outer_union))
		{
			append_trunk_face(face);
		}
		for (const TopoDS_Face& face : shape_faces(inner_trunk))
		{
			append_trunk_face(face);
		}
		if (outlet_source.faces.empty())
		{
			throw std::runtime_error("The collector trunk has no identifiable outlet opening face.");
		}
		replacement.sources.push_back(std::move(trunk_source));
		replacement.sources.push_back(std::move(outlet_source));

		for (size_t index = 0; index < inlet_count; ++index)
		{
			const fgcad_collector_inlet& inlet = inlets[index];
			if (!(inlet.merge_station > 0 && inlet.merge_station < 1)
				|| !(inlet.branch_start_handle_length > 0)
				|| !std::isfinite(inlet.merge_station)
				|| !std::isfinite(inlet.branch_start_handle_length))
			{
				throw std::invalid_argument(
					"Collector merge stations must lie in (0,1) and branch handles must be positive.");
			}
			auto inlet_radii = radii(inlet.profile);
			collector_profile_faces inlet_faces = profile_faces(inlet);
			double interface_overlap = std::max(
				0.5,
				inlet.profile.equivalent_radius > 0
					? inlet.profile.equivalent_radius * 0.02
					: inlet.profile.outer_diameter * 0.02
			);
			gp_Pnt p0 = point(inlet.frame.origin);
			gp_Dir inlet_tangent = unit(inlet.frame.tangent);
			gp_Pnt p1 = p0.Translated(
				gp_Vec(inlet_tangent) * inlet.branch_start_handle_length);
			gp_Pnt junction = outlet_origin.Translated(
				-gp_Vec(outlet_tangent)
				* (system->outlet_stub_length + inlet.merge_station * system->merge_length));
			gp_Pnt p3 = junction.Translated(
				gp_Vec(outlet_tangent) * system->overlap_length);
			double outlet_depth = gp_Vec(p3, outlet_origin).Dot(gp_Vec(outlet_tangent));
			if (!(outlet_depth > Precision::Confusion())
				|| outlet_depth >= trunk_length - Precision::Confusion())
			{
				throw std::invalid_argument(
					"Collector branch overlap must place P3 strictly inside the trunk.");
			}
			gp_Pnt p2 = p3.Translated(
				-gp_Vec(outlet_tangent) * system->branch_end_handle_length);
			double outer_radius = inlet_radii.first;
			fgcad_bezier_evaluation evaluation = evaluate_cubic_bezier_internal(
				inlet.frame,
				point(p1),
				point(p2),
				point(p3),
				outer_radius
			);
			(void)evaluation;

			TopoDS_Shape branch_outer = swept_volume(
				inlet.frame,
				p1,
				p2,
				p3,
				inlet_faces.outer,
				interface_overlap
			);
			TopoDS_Shape branch_inner = swept_volume(
				inlet.frame,
				p1,
				p2,
				p3,
				inlet_faces.inner,
				interface_overlap
			);
			gp_Pnt inlet_sample = section_inside_point(inlet_faces.inner);
			flow_samples.push_back(inlet_sample.Translated(
				gp_Vec(inlet_tangent)
					* std::max(Precision::Confusion() * 100.0, inlet_radii.second * 0.1)));
			if (!positive_common_volume(
				branch_inner,
				inner_trunk,
				std::max(trunk_length, inlet.profile.outer_diameter)))
			{
				throw std::runtime_error(
					"A collector branch has no positive gas-volume overlap with the trunk.");
			}
			outer_volumes.push_back(branch_outer);
			inner_volumes.push_back(branch_inner);
			replacement.runner_ids.push_back(require_text(inlet.runner_id, "runner_id"));

			runner_source inlet_source;
			inlet_source.id = require_text(inlet.inlet_id, "inlet_id");
			inlet_source.feature.kind = FGCAD_FEATURE_CUBIC_BEZIER;
			inlet_source.feature.entry_frame = inlet.frame;
			inlet_source.feature.exit_frame = system->outlet_frame;
			inlet_source.feature.control1 = point(p1);
			inlet_source.feature.control2 = point(p2);
			inlet_source.faces = shape_faces(branch_outer);
			for (const TopoDS_Face& face : shape_faces(branch_inner))
			{
				inlet_source.faces.push_back(face);
			}
			inlet_source.kind = FGCAD_SOURCE_COLLECTOR_INLET;
			inlet_source.owner_id = system_id;
			replacement.sources.push_back(std::move(inlet_source));
		}

		outer_union = fuse_all(outer_volumes, false, &replacement.sources);
		TopoDS_Shape inner_union = fuse_all(inner_volumes, false, nullptr);
		if (solid_count(inner_union) != 1)
		{
			throw std::runtime_error(
				"The collector gas-flow union is not one connected solid.");
		}
		TopExp_Explorer gas_solid(inner_union, TopAbs_SOLID);
		if (!gas_solid.More())
		{
			throw std::runtime_error("The collector gas-flow union contains no solid.");
		}
		TopoDS_Solid connected_gas = TopoDS::Solid(gas_solid.Current());
		for (const gp_Pnt& sample : flow_samples)
		{
			BRepClass3d_SolidClassifier classifier(
				connected_gas,
				sample,
				Precision::Confusion()
			);
			if (classifier.State() != TopAbs_IN)
			{
				throw std::runtime_error(
					"A collector opening does not lead into the connected gas-flow solid.");
			}
		}
		BRepAlgoAPI_Cut wall_cut(outer_union, inner_union);
		wall_cut.SetRunParallel(true);
		wall_cut.Build();
		if (!wall_cut.IsDone() || wall_cut.Shape().IsNull())
		{
			throw std::runtime_error("The collector wall subtraction failed.");
		}
		apply_boolean_history(wall_cut, replacement.sources);
		if (solid_count(wall_cut.Shape()) != 1)
		{
			throw std::runtime_error(
				"The collector wall is not one connected solid before runner fusion.");
		}
		std::vector<TopoDS_Shape> fused_volumes{ wall_cut.Shape() };
		for (size_t index = 0; index < replacement.runner_ids.size(); ++index)
		{
			const std::string& runner_id = replacement.runner_ids[index];
			auto staged_runner = document->staged_runners.find(runner_id);
			auto runner = document->runners.find(runner_id);
			if (staged_runner == document->staged_runners.end())
			{
				throw std::runtime_error(
					"Every collector member runner must be rebuilt in the staged generation.");
			}
			const runner_record* member = staged_runner != document->staged_runners.end()
				? &staged_runner->second
				: runner != document->runners.end() ? &runner->second : nullptr;
			if (member == nullptr || member->shape.IsNull())
			{
				throw std::out_of_range("A collector member runner has no valid exact solid.");
			}
			for (const runner_source& source : member->sources)
			{
				replacement.sources.push_back(source);
			}
			fused_volumes.push_back(member->shape);
		}
		if (document->staged_runners.size() != replacement.runner_ids.size())
		{
			throw std::runtime_error(
				"The staged generation contains runners outside this collector system.");
		}
		TopoDS_Shape fused = fuse_all(fused_volumes, true, &replacement.sources);
		if (!BRepCheck_Analyzer(fused, true).IsValid() || solid_count(fused) != 1)
		{
			throw std::runtime_error(
				"The fused runner/collector system is not one valid connected solid (solid count "
				+ std::to_string(solid_count(fused)) + ").");
		}
		replacement.shape = fused;
		for (const std::string& runner_id : replacement.runner_ids)
		{
			auto staged = document->staged_runners.find(runner_id);
			if (staged != document->staged_runners.end())
			{
				document->runners[runner_id] = std::move(staged->second);
			}
		}
		document->staged_runners.clear();
		document->staged_collector_id.clear();
		document->staged_generation_revision = 0;
		document->collectors[system_id] = std::move(replacement);
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_begin_collector_system_build(
	fgcad_document* document,
	const char* system_id,
	uint64_t generation_revision
)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(system_id, "system_id");
		if (!document->staged_collector_id.empty())
		{
			throw std::invalid_argument("Another collector-system build is already staged.");
		}
		document->staged_collector_id = std::move(id);
		document->staged_generation_revision = generation_revision;
		document->staged_runners.clear();
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_abort_collector_system_build(
	fgcad_document* document,
	const char* system_id,
	uint64_t generation_revision
)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		std::string id = require_text(system_id, "system_id");
		if (!document->staged_collector_id.empty()
			&& (document->staged_collector_id != id
				|| document->staged_generation_revision != generation_revision))
		{
			throw std::invalid_argument(
				"The collector staging abort does not match the active generation.");
		}
		document->staged_runners.clear();
		document->staged_collector_id.clear();
		document->staged_generation_revision = 0;
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_remove_collector_system(
	fgcad_document* document,
	const char* system_id
)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		document->collectors.erase(require_text(system_id, "system_id"));
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_rename_collector_system(
	fgcad_document* document,
	const char* system_id,
	const char* name
)
{
	return guarded([&]()
	{
		if (document == nullptr) throw std::invalid_argument("The document cannot be null.");
		auto found = document->collectors.find(require_text(system_id, "system_id"));
		if (found != document->collectors.end())
		{
			found->second.name = require_text(name, "name");
		}
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_tessellate_part(
	fgcad_document* document,
	const char* part_id,
	double linear_deflection,
	double angular_deflection,
	fgcad_tessellation** output
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || output == nullptr)
		{
			throw std::invalid_argument("Tessellation arguments cannot be null.");
		}

		auto result = tessellate(
			placed(find_part(*document, require_text(part_id, "part_id"))),
			linear_deflection,
			angular_deflection
		);
		*output = result.release();
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_tessellate_runner(
	fgcad_document* document,
	const char* runner_id,
	double linear_deflection,
	double angular_deflection,
	fgcad_tessellation** output
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || output == nullptr)
		{
			throw std::invalid_argument("Tessellation arguments cannot be null.");
		}

		auto found = document->runners.find(require_text(runner_id, "runner_id"));
		if (found == document->runners.end()) throw std::out_of_range("The runner was not found.");
		auto result = tessellate(found->second.shape, linear_deflection, angular_deflection, found->second.sources);
		*output = result.release();
		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_tessellate_collector_system(
	fgcad_document* document,
	const char* system_id,
	double linear_deflection,
	double angular_deflection,
	fgcad_tessellation** output
)
{
	return not_found_guarded([&]()
	{
		if (document == nullptr || output == nullptr)
		{
			throw std::invalid_argument("Tessellation arguments cannot be null.");
		}
		auto found = document->collectors.find(require_text(system_id, "system_id"));
		if (found == document->collectors.end())
		{
			throw std::out_of_range("The collector system was not found.");
		}
		auto result = tessellate(
			found->second.shape,
			linear_deflection,
			angular_deflection,
			found->second.sources
		);
		*output = result.release();
		return FGCAD_STATUS_OK;
	});
}

void fgcad_tessellation_destroy(fgcad_tessellation* tessellation)
{
	delete tessellation;
}

size_t fgcad_tessellation_vertex_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->vertices.size(); }
size_t fgcad_tessellation_index_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->indices.size(); }
size_t fgcad_tessellation_face_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->faces.size(); }
size_t fgcad_tessellation_source_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->sources.size(); }
size_t fgcad_tessellation_edge_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->edges.size(); }
size_t fgcad_tessellation_edge_point_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->edge_points.size(); }

fgcad_status fgcad_tessellation_copy(
	const fgcad_tessellation* value,
	fgcad_mesh_vertex* vertices,
	size_t vertex_capacity,
	uint32_t* indices,
	size_t index_capacity,
	fgcad_face_range* faces,
	size_t face_capacity,
	fgcad_geometry_source_ref* sources,
	size_t source_capacity,
	fgcad_edge_range* edges,
	size_t edge_capacity,
	fgcad_point3* edge_points,
	size_t edge_point_capacity,
	fgcad_point3* minimum,
	fgcad_point3* maximum
)
{
	return guarded([&]()
	{
		if (value == nullptr || minimum == nullptr || maximum == nullptr)
		{
			throw std::invalid_argument("Tessellation copy arguments cannot be null.");
		}

		if (vertex_capacity < value->vertices.size()
			|| index_capacity < value->indices.size()
			|| face_capacity < value->faces.size()
			|| source_capacity < value->sources.size()
			|| edge_capacity < value->edges.size()
			|| edge_point_capacity < value->edge_points.size())
		{
			throw std::invalid_argument("A tessellation destination buffer is too small.");
		}

		std::copy(value->vertices.begin(), value->vertices.end(), vertices);
		std::copy(value->indices.begin(), value->indices.end(), indices);
		std::copy(value->faces.begin(), value->faces.end(), faces);
		std::copy(value->sources.begin(), value->sources.end(), sources);
		std::copy(value->edges.begin(), value->edges.end(), edges);
		std::copy(value->edge_points.begin(), value->edge_points.end(), edge_points);
		*minimum = value->minimum;
		*maximum = value->maximum;
		return FGCAD_STATUS_OK;
	});
}

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
}
