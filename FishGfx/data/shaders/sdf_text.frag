#version 410

layout (location = 0) in vec4 Clr;
layout (location = 1) in vec2 UV;

uniform sampler2D Texture;
uniform float AlphaTest;
uniform float SdfPixelRange;

layout (location = 0) out vec4 OutClr;

void main() {
	float distance = texture(Texture, UV).r;
	vec2 unitRange = vec2(SdfPixelRange) / vec2(textureSize(Texture, 0));
	vec2 screenTextureSize = vec2(1.0) / max(fwidth(UV), vec2(0.000001));
	float screenPixelRange = max(0.5 * dot(unitRange, screenTextureSize), 1.0);
	float coverage = clamp(screenPixelRange * (distance - 0.5) + 0.5, 0.0, 1.0);
	coverage = smoothstep(0.15, 0.85, coverage);
	if (coverage <= 0.001)
		discard;
	float alpha = Clr.a * coverage;
	if (alpha < AlphaTest)
		discard;
	OutClr = vec4(Clr.rgb, alpha);
}
