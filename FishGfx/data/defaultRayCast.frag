#version 400

in vec4 frag_Clr;
in vec2 frag_UV;

uniform sampler2D Texture;

layout (location = 0) out vec4 OutClr;
layout (location = 1) out vec4 RayClr;

void main() {
	vec4 TexClr = texture(Texture, frag_UV);

	OutClr = TexClr;
	RayClr = frag_Clr;
}
