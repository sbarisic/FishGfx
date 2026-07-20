#version 430

in vec2 frag_UV;
in float frag_AlphaCutoff;
flat in int frag_TextureLayer;

uniform sampler2DArray CubeBaseColor;
uniform sampler2D ModelAtlas;
uniform float AlphaCutoff;
uniform int UseVertexAlphaCutoff;

void main()
{
	float cutoff = UseVertexAlphaCutoff != 0 ? frag_AlphaCutoff : AlphaCutoff;

	float alpha = frag_TextureLayer >= 0
		? texture(CubeBaseColor, vec3(frag_UV, float(frag_TextureLayer))).a
		: texture(ModelAtlas, frag_UV).a;

	if (alpha < cutoff)
	{
		discard;
	}
}
