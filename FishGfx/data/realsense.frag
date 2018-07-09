#version 410
#extension GL_ARB_explicit_uniform_location : enable

layout (location = 0) in vec4 Clr;
layout (location = 1) in vec2 UV;

uniform sampler2D ColorTexture;
uniform sampler2D DepthTexture;

layout (location = 0) out vec4 OutClr;

void main() {
	if (UV.x < 0.0f || UV.x > 1.0f || UV.y < 0.0f || UV.y > 1.0f)
		discard;

	vec4 Color = Clr * texture(ColorTexture, UV);
	OutClr = Color;

	//float DepthScale = 5;
	//float Depth = texture(DepthTexture, UV).r * DepthScale;

	//vec4 DepthClr = vec4(Depth, Depth, Depth, 1.0f);
	//if (Depth * 255 < 10)
	//	DepthClr = Color;

	//OutClr = DepthClr;
	//OutClr = vec4(1.0f, 0.0f, 0.0f, 1.0f);
}