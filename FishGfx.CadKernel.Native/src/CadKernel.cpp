#include "FishGfxCadKernel.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <limits>
#include <memory>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

#include <BRepAdaptor_Curve.hxx>
#include <BRepAdaptor_Surface.hxx>
#include <BRepAlgoAPI_Cut.hxx>
#include <BRepAlgoAPI_Fuse.hxx>
#include <BRepBndLib.hxx>
#include <BRepBuilderAPI_MakeEdge.hxx>
#include <BRepBuilderAPI_MakeFace.hxx>
#include <BRepBuilderAPI_MakeWire.hxx>
#include <BRepCheck_Analyzer.hxx>
#include <BRepGProp.hxx>
#include <BRepMesh_IncrementalMesh.hxx>
#include <BRepOffsetAPI_MakePipe.hxx>
#include <BRepOffsetAPI_MakeOffset.hxx>
#include <BRepOffsetAPI_ThruSections.hxx>
#include <BRepTools.hxx>
#include <BRepTools_WireExplorer.hxx>
#include <BRep_Builder.hxx>
#include <BRep_Tool.hxx>
#include <BinXCAFDrivers.hxx>
#include <Bnd_Box.hxx>
#include <GC_MakeArcOfCircle.hxx>
#include <GProp_GProps.hxx>
#include <GeomAbs_CurveType.hxx>
#include <GeomAbs_SurfaceType.hxx>
#include <GeomAbs_JoinType.hxx>
#include <Geom_Curve.hxx>
#include <Interface_Static.hxx>
#include <NCollection_List.hxx>
#include <NCollection_Sequence.hxx>
#include <PCDM_ReaderStatus.hxx>
#include <PCDM_StoreStatus.hxx>
#include <Poly.hxx>
#include <Poly_Triangle.hxx>
#include <Poly_Triangulation.hxx>
#include <STEPCAFControl_Reader.hxx>
#include <STEPCAFControl_Writer.hxx>
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
#include <TopoDS_Wire.hxx>
#include <XCAFApp_Application.hxx>
#include <XCAFDoc_DocumentTool.hxx>
#include <XCAFDoc_Editor.hxx>
#include <XCAFDoc_ShapeTool.hxx>
#include <gp_Ax2.hxx>
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
};

struct runner_record
{
	std::string id;
	std::string name;
	TopoDS_Shape shape;
	std::vector<runner_source> sources;
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
			info.radius = std::sqrt(area / pi);
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
	const std::unordered_map<std::string, selector_record>& selectors
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

	for (const auto& entry : runners)
	{
		const runner_record& runner = entry.second;
		if (runner.shape.IsNull()) continue;
		TDF_Label definition = shapes->AddShape(runner.shape, false);
		TDF_Label label = shapes->AddComponent(assembly, definition, TopLoc_Location());
		TDataStd_Name::Set(label, extended("FGRUNNER:" + runner.id + ":" + runner.name));
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

double squared_distance(const gp_Pnt& a, const fgcad_point3& b)
{
	double x = a.X() - b.x;
	double y = a.Y() - b.y;
	double z = a.Z() - b.z;
	return x * x + y * y + z * z;
}

std::string closest_source(
	const TopoDS_Face& face,
	const std::vector<runner_source>& sources
)
{
	if (sources.empty())
	{
		return {};
	}

	for (const runner_source& source : sources)
	{
		for (const TopoDS_Face& source_face : source.faces)
		{
			if (face.IsSame(source_face))
			{
				return source.id;
			}
		}
	}

	GProp_GProps properties;
	BRepGProp::SurfaceProperties(face, properties);
	gp_Pnt center = properties.CentreOfMass();
	double best = std::numeric_limits<double>::infinity();
	std::string result;

	for (const runner_source& source : sources)
	{
		double distance = std::min(
			squared_distance(center, source.feature.entry_frame.origin),
			squared_distance(center, source.feature.exit_frame.origin)
		);

		if (source.feature.kind == FGCAD_FEATURE_BEND)
		{
			distance = std::min(distance, squared_distance(center, source.feature.center));
		}

		if (distance < best)
		{
			best = distance;
			result = source.id;
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

void apply_fuse_history(BRepAlgoAPI_Fuse& fuse, std::vector<runner_source>& sources)
{
	for (runner_source& source : sources)
	{
		std::vector<TopoDS_Face> mapped;
		for (const TopoDS_Face& face : source.faces)
		{
			const NCollection_List<TopoDS_Shape>& modified = fuse.Modified(face);
			if (!modified.IsEmpty())
			{
				for (NCollection_List<TopoDS_Shape>::Iterator iterator(modified); iterator.More(); iterator.Next())
				{
					if (iterator.Value().ShapeType() == TopAbs_FACE)
					{
						mapped.push_back(TopoDS::Face(iterator.Value()));
					}
				}
			}
			else if (!fuse.IsDeleted(face))
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
	std::unordered_map<std::string, selector_record> selectors;
};

struct fgcad_tessellation
{
	std::vector<fgcad_mesh_vertex> vertices;
	std::vector<uint32_t> indices;
	std::vector<fgcad_face_range> faces;
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
		copy_id(range.source_node_id, closest_source(face, sources));
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
}

extern "C"
{
uint32_t fgcad_api_version(void)
{
	return 3;
}

const char* fgcad_last_error(void)
{
	return last_error.c_str();
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
			return TopoDS::Wire(result.Moved(TopLoc_Location(part.placement)));
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
			throw std::invalid_argument("A profile transition does not have a sweep edge.");
		};

		auto make_sweep = [&](const fgcad_runner_feature& feature)
		{
			BRepBuilderAPI_MakeWire wire(feature_edge(feature));
			BRepOffsetAPI_MakePipe pipe(wire.Wire(), annular_face(profile_at(feature.input_profile, feature.entry_frame)));
			if (!pipe.IsDone()) throw std::runtime_error("Open CASCADE could not sweep a runner feature.");
			return pipe.Shape();
		};

		auto make_loft = [&](const fgcad_runner_feature& feature)
		{
			profile_wires input = profile_at(feature.input_profile, feature.entry_frame);
			profile_wires output = profile_at(feature.output_profile, feature.exit_frame);
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
			if (!outer.IsDone() || !inner.IsDone()) throw std::runtime_error("Open CASCADE could not loft the profile transition.");
			BRepAlgoAPI_Cut cut(outer.Shape(), inner.Shape());
			cut.Build();
			if (!cut.IsDone()) throw std::runtime_error("The hollow profile transition could not be cut.");
			return cut.Shape();
		};

		runner_record replacement;
		replacement.id = require_text(runner_id, "runner_id");
		replacement.name = require_text(runner_name, "runner_name");
		TopoDS_Shape result;
		for (size_t index = 0; index < feature_count; ++index)
		{
			const fgcad_runner_feature& feature = features[index];
			TopoDS_Shape section = feature.kind == FGCAD_FEATURE_LOFT_TRANSITION
				? make_loft(feature)
				: make_sweep(feature);
			if (section.IsNull() || !BRepCheck_Analyzer(section, true).IsValid())
			{
				throw std::runtime_error("A generated runner feature failed exact B-rep validation.");
			}
			runner_source source;
			source.id = feature.source_node_id;
			source.feature = feature;
			source.faces = shape_faces(section);
			replacement.sources.push_back(std::move(source));

			if (result.IsNull()) result = section;
			else
			{
				BRepAlgoAPI_Fuse fuse(result, section);
				fuse.Build();
				if (!fuse.IsDone()) throw std::runtime_error("Adjacent runner features could not be joined.");
				apply_fuse_history(fuse, replacement.sources);
				result = fuse.Shape();
				if (result.IsNull() || !BRepCheck_Analyzer(result, true).IsValid())
				{
					throw std::runtime_error("An intermediate runner join failed exact B-rep validation.");
				}
			}
		}

		if (result.IsNull() || !BRepCheck_Analyzer(result, true).IsValid())
		{
			throw std::runtime_error("The complete runner failed exact B-rep validation.");
		}

		replacement.shape = result;
		document->runners[replacement.id] = std::move(replacement);
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

void fgcad_tessellation_destroy(fgcad_tessellation* tessellation)
{
	delete tessellation;
}

size_t fgcad_tessellation_vertex_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->vertices.size(); }
size_t fgcad_tessellation_index_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->indices.size(); }
size_t fgcad_tessellation_face_count(const fgcad_tessellation* value) { return value == nullptr ? 0 : value->faces.size(); }
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
			|| edge_capacity < value->edges.size()
			|| edge_point_capacity < value->edge_points.size())
		{
			throw std::invalid_argument("A tessellation destination buffer is too small.");
		}

		std::copy(value->vertices.begin(), value->vertices.end(), vertices);
		std::copy(value->indices.begin(), value->indices.end(), indices);
		std::copy(value->faces.begin(), value->faces.end(), faces);
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
		Handle(TDocStd_Document) xcaf = make_xcaf_document(document->parts, document->runners, document->selectors);
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

			if (name == "FGRUNNER" || name.rfind("FGRUNNER:", 0) == 0)
			{
				runner_record runner;
				if (name == "FGRUNNER")
				{
					runner.id = "legacy-runner";
					runner.name = "Runner 1";
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

		return FGCAD_STATUS_OK;
	});
}

fgcad_status fgcad_document_export_step_ap242(fgcad_document* document, const char* path_utf8)
{
	return guarded([&]()
	{
		if (document == nullptr || document->runners.empty()
			|| std::any_of(document->runners.begin(), document->runners.end(), [](const auto& item)
			{
				return item.second.shape.IsNull();
			}))
		{
			throw std::invalid_argument("A valid exact runner is required before STEP export.");
		}

		Handle(TDocStd_Document) xcaf = make_xcaf_document(document->parts, document->runners, document->selectors);
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
