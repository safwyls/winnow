// Shared filter uniforms must have identical precision in both stages.
export const vertex = `
precision highp float;
in vec2 aPosition;
out vec2 vTextureCoord;
uniform vec4 uInputSize;
uniform vec4 uOutputFrame;
uniform vec4 uOutputTexture;
void main() {
  vec2 position = aPosition * uOutputFrame.zw + uOutputFrame.xy;
  position.x = position.x * (2.0 / uOutputTexture.x) - 1.0;
  position.y = position.y * (2.0 * uOutputTexture.z / uOutputTexture.y) - uOutputTexture.z;
  gl_Position = vec4(position, 0.0, 1.0);
  vTextureCoord = aPosition * uOutputFrame.zw * uInputSize.zw;
}`

// Only shade the rim. Artwork stays in the host's cached, decoded DOM image.
export const fragment = `
precision highp float;
in vec2 vTextureCoord;
out vec4 finalColor;
uniform vec4 uInputSize;
uniform vec2 uCenter;
uniform vec2 uRadius;
uniform vec2 uShape;
uniform float uTime;
uniform vec3 uColorA;
uniform vec3 uColorB;
void main() {
  vec2 q = (vTextureCoord * uInputSize.xy - uCenter) / uRadius;
  float angle = atan(q.y, q.x);
  float contour = pow(pow(abs(q.x), uShape.x) + pow(abs(q.y), uShape.x), 1.0 / uShape.x);
  float ripple = (sin(angle * 3.0 + uTime * .32) + sin(angle * 7.0 - uTime * .23) * .62) * uShape.y;
  float distance = (contour - 1.0) * min(uRadius.x, uRadius.y) + ripple;
  float edge = exp(-abs(distance) * .8);
  float halo = exp(-abs(distance) * .13) * .18;
  float shift = .5 + .5 * sin(angle * 2.0 + uTime * .19);
  vec3 rim = mix(uColorA, uColorB, shift);
  float alpha = clamp(edge * .8 + halo, 0.0, 1.0);
  finalColor = vec4(rim * alpha, alpha);
}`
