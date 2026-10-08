# Промпты спрайтов танков

Дата генерации: 2026-10-07. Инструмент: встроенный `image_gen`. Документ хранится как архив раннего графического процесса; прежний эталон БТ-2 удалён из актуальных ассетов 2026-10-08.

## Tiger II — точная переработка по референсу, 2026-10-08

В новой версии нейросеть не разделяет и не перерисовывает видимые детали. Прозрачный танк получен отдельным проходом удаления фона, затем исходные пиксели башни, маски и полного ствола вырезаны единой маской. Для скрытой палубы использован только следующий служебный запрос:

```text
Edit the supplied transparent top-down Tiger II image. This is a narrowly scoped reconstruction pass, not a redesign. Remove only the complete turret assembly, mantlet, and gun barrel, and reconstruct the small hull-deck area that was hidden directly underneath the turret using the adjacent armor plates, camouflage colors, deck texture, and lighting. Keep the hull at exactly the same orientation, placement, scale, silhouette, proportions, color, tracks, engine deck, tools, grilles, hatches, and all other visible details. Keep the background genuinely transparent. Do not rotate, crop, resize, restyle, repaint, simplify, add labels, or invent a different tank. The result is only a donor image for the hidden hull patch; preserve everything outside that patch as closely as possible.
```

После этого корпус и башенная группа собраны детерминированно с одним общим масштабом; генеративное разделение деталей не применялось.

## Tiger II

```text
Use case: stylized-concept
Asset type: Unity 2D top-down tank sprite sheet
Primary request: Redraw a historically recognizable German Tiger II heavy tank using the supplied BT-2 image only as the style, palette, texture-density, lighting, composition, orientation, and scale reference.
Input image: style/composition reference only; do not copy BT-2 vehicle anatomy.
Scene/backdrop: genuinely transparent background, no ground and no cast shadow.
Subject: Tiger II (Königstiger), production turret, accurate broad sloped hull, interleaved running gear impression, engine deck, hatches, large angular turret and long 8.8 cm KwK 43 gun.
Style/medium: detailed muted beige-grey painted metal matching the BT-2 reference, realistic but readable game sprite.
Composition/framing: strict flat orthographic view from directly overhead; bare hull vertically aligned on the left, detached turret with gun vertically aligned on the right; clear transparent gap; neither component touches, overlaps, or is cropped; use the full portrait canvas similarly to the reference.
Lighting/mood: subtle neutral overhead shading only, no directional ground shadow.
Constraints: exactly one bare hull and exactly one detached turret-and-gun assembly; actual alpha transparency; clean edges; recognizable real-world proportions.
Avoid: perspective, isometric angle, side view, labels, text, insignia, flags, crew, scenery, borders, grid, watermark, duplicate parts.
```

## E-100

```text
Use case: stylized-concept
Asset type: Unity 2D top-down tank sprite sheet
Primary request: Redraw the German E-100 super-heavy tank project using the supplied BT-2 image only as the style, palette, texture-density, lighting, composition, orientation, and scale reference.
Input image: style/composition reference only; do not copy BT-2 vehicle anatomy.
Scene/backdrop: genuinely transparent background, no ground and no cast shadow.
Subject: E-100 with historically plausible massive wide hull, heavy tracks, engine deck and the established angular project turret carrying the very large 15 cm gun; clearly bulkier and heavier than Tiger II.
Style/medium: detailed muted beige-grey painted metal matching the BT-2 reference, realistic but readable game sprite.
Composition/framing: strict flat orthographic view from directly overhead; bare hull vertically aligned on the left, detached turret with gun vertically aligned on the right; clear transparent gap; neither component touches, overlaps, or is cropped; use the full portrait canvas similarly to the reference.
Lighting/mood: subtle neutral overhead shading only, no directional ground shadow.
Constraints: exactly one bare hull and exactly one detached turret-and-gun assembly; actual alpha transparency; clean edges; recognizable project proportions.
Avoid: perspective, isometric angle, side view, labels, text, insignia, flags, crew, scenery, borders, grid, watermark, duplicate parts.
```

После проверки E-100 применён один корректирующий запрос:

```text
Use case: precise-object-edit
Asset type: Unity 2D top-down tank sprite sheet
Primary request: Correct only the composition of the supplied E-100 sprite sheet so both components have a clean transparent safety margin on every canvas edge.
Input images: Image 1 is the edit target E-100 sprite; Image 2 is the BT-2 style/composition reference.
Keep unchanged: E-100 vehicle identity, strict flat orthographic overhead view, detailed muted beige-grey metal style, all hull/turret details, one bare hull on the left, one detached turret with its long gun on the right, transparent background.
Change only: scale the two existing components down slightly and center them so no track, barrel, turret, antialiasing, or shadow touches or crosses any canvas edge; leave at least 3% transparent margin around the entire occupied artwork and retain a clear transparent gap between hull and turret.
Constraints: genuine alpha transparency; no ground, no cast shadow, no added objects, no text, no crop, no overlap, no perspective.
```

## T-34/76

```text
Use case: stylized-concept
Asset type: Unity 2D top-down tank sprite sheet
Primary request: Redraw a historically recognizable Soviet T-34/76 medium tank with the 1942-style hexagonal turret and F-34 gun, using the supplied BT-2 image only as the style, palette, texture-density, lighting, composition, orientation, and scale reference.
Input image: style/composition reference only; do not copy BT-2 vehicle anatomy.
Scene/backdrop: genuinely transparent background, no ground and no cast shadow.
Subject: T-34/76, accurate sloped hull, prominent tracks and five large road wheels per side impression, rear engine grilles, hatches, hexagonal turret and 76.2 mm gun.
Style/medium: detailed muted beige-grey/olive painted metal matching the BT-2 reference, realistic but readable game sprite.
Composition/framing: strict flat orthographic view from directly overhead; bare hull vertically aligned on the left, detached turret with gun vertically aligned on the right; clear transparent gap; neither component touches, overlaps, or is cropped; use the full portrait canvas similarly to the reference.
Lighting/mood: subtle neutral overhead shading only, no directional ground shadow.
Constraints: exactly one bare hull and exactly one detached turret-and-gun assembly; actual alpha transparency; clean edges; recognizable real-world proportions.
Avoid: perspective, isometric angle, side view, labels, text, insignia, flags, crew, scenery, borders, grid, watermark, duplicate parts.
```

## Panzer IV

```text
Use case: stylized-concept
Asset type: Unity 2D top-down tank sprite sheet
Primary request: Redraw a historically recognizable German Panzerkampfwagen IV medium tank, long-gun Ausf. G configuration with 7.5 cm KwK 40 L/43, using the supplied BT-2 image only as the style, palette, texture-density, lighting, composition, orientation, and scale reference.
Input image: style/composition reference only; do not copy BT-2 vehicle anatomy.
Scene/backdrop: genuinely transparent background, no ground and no cast shadow.
Subject: Panzer IV, accurate boxy hull, narrow tracks and return rollers impression, engine deck and hatches, angular turret with commander cupola and long 7.5 cm gun; no side skirts.
Style/medium: detailed muted beige-grey painted metal matching the BT-2 reference, realistic but readable game sprite.
Composition/framing: strict flat orthographic view from directly overhead; bare hull vertically aligned on the left, detached turret with gun vertically aligned on the right; clear transparent gap; neither component touches, overlaps, or is cropped; use the full portrait canvas similarly to the reference.
Lighting/mood: subtle neutral overhead shading only, no directional ground shadow.
Constraints: exactly one bare hull and exactly one detached turret-and-gun assembly; actual alpha transparency; clean edges; recognizable real-world proportions.
Avoid: perspective, isometric angle, side view, labels, text, insignia, flags, crew, scenery, borders, grid, watermark, duplicate parts.
```
