# Xiaodu Lulu look mechanics

## Natural motion

Xiaodu Lulu looks around with the large physical eyeballs first, followed by a small head-and-muzzle turn and restrained upper-torso follow-through. The feet, shorts, lower belly, scale, and baseline stay anchored. The tiny cap and ears remain attached and follow the head subtly; they never jump, flip sides, or rotate independently. Do not rotate, skew, or tilt the complete sprite.

The eyeballs behave as physical globes: sclera, iris, pupil, eyelids, rim, and highlights turn together inside the original eye construction. Do not paste new eyes or slide detached pupils over fixed whites. Preserve the friendly asymmetry of the source eyes and the exact orange muzzle, pale-yellow body, brown-orange shorts, short legs, and soft 3D toy material.

## Cardinal pose families

- `000 up`: both eyes clearly aim above the head center; eyelids open toward the upper gaze, the muzzle tips slightly upward, and the upper head follows without lifting the feet or changing body scale.
- `090 screen-right`: pupils, eye surfaces, muzzle/nose direction, and head turn read unmistakably toward the image's right edge. The screen-left side of the face becomes slightly more visible while the far side occludes naturally.
- `180 down`: both eyes and eyelids aim below the head center; the muzzle dips and the upper head follows downward while the grounded lower body remains unchanged.
- `270 screen-left`: pupils, eye surfaces, muzzle/nose direction, and head turn read unmistakably toward the image's left edge. The screen-right side of the face becomes slightly more visible while the far side occludes naturally.

## Motion budget and continuity

Every `22.5` degree step moves the eyes, muzzle, head, ears, cap, and upper torso by roughly one equal visual increment. Keep the lower-body anchor, baseline, body height, and shorts registration fixed. Diagonals combine the adjacent horizontal and vertical families; no adjacent pair may introduce a scale pop, sudden bend, identity change, eye replacement, cap jump, or reversal. `157.5 -> 180`, `337.5 -> 000`, and the boundary between the two generated rows must each be a single smooth step.

The neutral/rest frame is not `000`; the application uses idle inside the pointer deadzone. Every generated direction must remain visibly distinct from neutral at final pet size.
