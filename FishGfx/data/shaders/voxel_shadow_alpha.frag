#version 430

in vec2 frag_UV;
in float frag_AlphaCutoff;

uniform sampler2D Texture0;
uniform float AlphaCutoff;
uniform int UseVertexAlphaCutoff;

void main()
{
	float cutoff = UseVertexAlphaCutoff != 0 ? frag_AlphaCutoff : AlphaCutoff;

	if (texture(Texture0, frag_UV).a < cutoff)
	{
		discard;
	}
}
