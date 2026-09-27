// Both stages use matching precision for shared Pixi filter uniforms.
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
    }
  `

export const fragment = `
    precision highp float;
    in vec2 vTextureCoord;
    out vec4 finalColor;
    uniform vec4 uInputSize;
    uniform sampler2D uArtwork;
    uniform vec2 uArtworkSize;
    uniform vec2 uSize;
    uniform vec2 uCursor;
    uniform float uReveal;
    uniform float uIntensity;
    uniform float uFinish;
    uniform float uFoilStrength;
    uniform float uFoilThreshold;
    uniform float uFoilMetal;

    float luminance(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

    void main() {
      vec2 p = vTextureCoord * uInputSize.xy;
      vec2 uv = p / uSize;
      // Match the DOM cover's centered crop exactly. Light never displaces the print.
      float fit = max(uSize.x / uArtworkSize.x, uSize.y / uArtworkSize.y);
      vec2 artUv = clamp((p - uSize * 0.5) / (uArtworkSize * fit) + 0.5, 0.001, 0.999);
      vec4 artwork = texture(uArtwork, artUv);
      vec3 ink = artwork.rgb;
      float luma = luminance(ink);
      vec2 cursor = uCursor / uSize;
      vec2 delta = (p - uCursor) / uSize.x;
      float light = exp(-dot(delta, delta) * 1.8);
      vec3 pigment = mix(vec3(luma), ink, 1.0 + light * 0.12 * uIntensity);
      vec3 lit = pigment * (1.0 + light * 0.17 * uIntensity);

      // Fine contrast in the artwork breaks the reflection, like printed ink under satin.
      vec2 texel = 1.5 / uArtworkSize;
      float grain = abs(luminance(texture(uArtwork, artUv + vec2(texel.x, 0.0)).rgb) - luma)
                  + abs(luminance(texture(uArtwork, artUv + vec2(0.0, texel.y)).rgb) - luma);
      float slope = -0.35 + (cursor.x - 0.5) * 0.25;
      float diagonal = (uv.x - cursor.x) + (uv.y - cursor.y) * slope;
      float satin = exp(-diagonal * diagonal * 18.0) * light;
      float gloss = satin * (0.20 + min(grain, 0.2) * 0.5);
      vec3 reflection = mix(vec3(0.92, 0.86, 0.77), normalize(ink + 0.25), 0.34);

      if (uFinish > 1.5) {
        float spectrum = diagonal * 5.8 + cursor.x * 1.7 - cursor.y * 0.9;
        vec3 iridescence = 0.64 + 0.36 * cos(spectrum + vec3(0.0, 2.1, 4.2));
        reflection = mix(reflection, iridescence, 0.65);
        gloss = satin * 0.40 + pow(satin, 5.0) * 0.12;
      }
      if (uFinish < 0.5) gloss = 0.0;

      // Screen-like reflection preserves deep pigments and avoids blowing out white logos.
      lit += (1.0 - lit) * reflection * gloss * uIntensity * (1.0 - luma * 0.35);
      float edge = min(min(p.x, uSize.x - p.x), min(p.y, uSize.y - p.y));
      float glancing = exp(-max(edge, 0.0) * 0.75) * light * step(0.5, uFinish);
      lit += (1.0 - lit) * reflection * glancing * 0.16 * uIntensity;

      // Select from the original print, never the illuminated result: reflections cannot
      // expand their own mask. Feather the boundary so pale gradients do not become cutouts.
      float foilMask = smoothstep(uFoilThreshold - 0.08, min(1.0, uFoilThreshold + 0.08), luma);
      float sweep = uv.x * 0.85 + uv.y * 0.45 - 0.65
                  - (cursor.x - 0.5) * 0.8 + (cursor.y - 0.5) * 0.45;
      float broad = exp(-sweep * sweep * 10.0);
      float glint = exp(-sweep * sweep * 190.0);
      vec3 metalTint = vec3(0.88, 0.93, 1.0);
      if (uFoilMetal > 0.5 && uFoilMetal < 1.5) metalTint = vec3(1.0, 0.74, 0.32);
      if (uFoilMetal > 1.5) {
        metalTint = 0.68 + 0.32 * cos(sweep * 13.0 + cursor.x * 2.0 + vec3(0.0, 2.1, 4.2));
      }
      // Metal needs darker reflections as well as bright ones; adding white to white alone
      // cannot show a finish. Keep some original pigment and fine printed contrast.
      vec3 foil = metalTint * (0.43 + broad * 0.40) + vec3(glint * 0.34);
      foil *= 0.88 + luma * 0.12;
      foil = mix(foil, ink, 0.16);
      lit = mix(lit, clamp(foil, 0.0, 1.0), foilMask * uFoilStrength);
      float alpha = artwork.a * uReveal;
      finalColor = vec4(clamp(lit, 0.0, 1.0) * alpha, alpha);
    }
  `
