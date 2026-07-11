#version 400

layout (location = 0) in vec3 Pos;
layout (location = 1) in vec4 Clr;
layout (location = 2) in vec2 UV;
layout (location = 3) in vec3 Normal;

out vec4 frag_Clr;
out vec2 frag_UV;
out vec3 frag_Normal;
out vec3 frag_WorldPosition;

uniform mat4 Model;
uniform mat4 View;
uniform mat4 Project;

void main()
{
	vec4 worldPosition = Model * vec4(Pos, 1.0);
	frag_Clr = Clr;
	frag_UV = UV;
	frag_Normal = normalize(mat3(Model) * Normal);
	frag_WorldPosition = worldPosition.xyz;
	gl_Position = Project * View * worldPosition;
}
