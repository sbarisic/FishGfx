#version 400

in vec4 gColor;
in vec2 gCenterOffset;
in float gScaledThickness;

layout (location = 0) out vec4 outColor;

void main()
{
	const float edgePixels = 4.0;
	const float minimumSmoothedSize = 8.0;
	const float minimumHardEdgeSize = 1.5;

	vec2 offsetSquared = gCenterOffset * gCenterOffset;
	float distanceSquared = offsetSquared.x + offsetSquared.y;
	float hardAlpha = 1.0 - step(1.0, distanceSquared);
	float sizeAwareHardAlpha = mix(
		1.0,
		hardAlpha,
		step(minimumHardEdgeSize, gScaledThickness)
	);
	float smoothAlpha = 1.0 - smoothstep(
		1.0 - edgePixels / gScaledThickness,
		1.0,
		distanceSquared
	);
	float alpha = mix(
		sizeAwareHardAlpha,
		smoothAlpha,
		step(minimumSmoothedSize, gScaledThickness)
	);

	outColor = vec4(gColor.rgb, min(gColor.a, alpha));
}
