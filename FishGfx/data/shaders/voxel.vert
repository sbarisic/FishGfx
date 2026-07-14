#version 400

layout (location = 0) in vec3 Pos;
layout (location = 1) in vec4 Clr;
layout (location = 2) in vec2 UV;
layout (location = 3) in vec3 Normal;
layout (location = 5) in vec4 Light;

out vec4 frag_Clr;
out vec2 frag_UV;
out vec3 frag_Normal;
out vec3 frag_WorldPosition;
out vec4 frag_Light;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
	vec4 worldPosition = uModel * vec4(Pos, 1.0);
	frag_Clr = Clr;
	frag_UV = UV;
	frag_Normal = normalize(mat3(uModel) * Normal);
	frag_WorldPosition = worldPosition.xyz;
	frag_Light = Light;
	gl_Position = uProjection * uView * worldPosition;
}
