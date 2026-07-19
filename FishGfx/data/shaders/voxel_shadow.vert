#version 430

layout (location = 0) in vec3 Pos;
layout (location = 2) in vec2 UV;
layout (location = 5) in vec4 ShadowData;
layout (location = 7) in vec3 ChunkOrigin;

out vec2 frag_UV;
out float frag_AlphaCutoff;

uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
	frag_UV = UV;
	frag_AlphaCutoff = ShadowData.x;
	gl_Position = uProjection * uView * vec4(Pos + ChunkOrigin, 1.0);
}
