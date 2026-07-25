// Internal exact-curve evaluation and validation. Included inside the private namespace.

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
	gp_Vec chord(p0, p3);
	double straight_tolerance = std::max(
		Precision::Confusion(),
		control_polygon_length * 1.0e-12);
	bool is_straight = chord.Magnitude() > Precision::Confusion()
		&& gp_Vec(p0, p1).Crossed(chord).Magnitude()
			<= straight_tolerance * chord.Magnitude()
		&& gp_Vec(p0, p2).Crossed(chord).Magnitude()
			<= straight_tolerance * chord.Magnitude();
	if (!is_straight && !certify_curvature(
		derivative,
		second_derivative,
		outer_radius + Precision::Confusion(),
		0,
		minimum_radius))
	{
		throw std::invalid_argument(
			"The cubic Bezier curvature cannot be certified for the active outer profile radius "
			"(control cross magnitudes "
				+ std::to_string(gp_Vec(p0, p1).Crossed(chord).Magnitude())
				+ ", "
				+ std::to_string(gp_Vec(p0, p2).Crossed(chord).Magnitude())
				+ "; straight tolerance "
				+ std::to_string(straight_tolerance * chord.Magnitude())
				+ ").");
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
