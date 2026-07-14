#version 400

layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec4 aColor;

out vec4 vColor;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProjection;

void main()
{
	vColor = aColor;

	mat4 modelViewProjection = uProjection * uView * uModel;
	gl_Position = modelViewProjection * vec4(aPosition, 0.0, 1.0);
}
