import { animatedSpriteScene } from './animatedSprite';
import { asteroidsScene } from './asteroids';
import { assetBundleScene } from './assetBundle';
import { basicSpriteScene } from './basicSprite';
import { bitmapText2Scene } from './bitmapText2';
import { bitmapTextScene } from './bitmapText';
import { blendModesScene } from './blendModes';
import { blurFilterScene } from './blurFilter';
import { breakoutScene } from './breakout';
import { containerPivotScene } from './containerPivot';
import { containerScene } from './container';
import { draggingScene } from './dragging';
import { ecsSpritesScene } from './ecsSprites';
import { fromFontScene } from './fromFont';
import { graphicsMaskScene } from './graphicsMask';
import { meshRopeScene } from './meshRope';
import { pixiTextScene } from './pixiText';
import { renderTextureScene } from './renderTexture';
import { simpleGraphicsScene } from './simpleGraphics';
import { snakeScene } from './snake';
import { starWarpScene } from './starWarp';
import { tetrisScene } from './tetris';
import { tilingSpriteScene } from './tilingSprite';

import type { SceneBuilder } from './types';

/**
 * Scene registry. Keys MUST match ExampleInfo.Id in Game.Examples/ExamplesCatalog.cs.
 * C# is the source of truth; a missing key here logs an error and renders nothing.
 */
export const sceneRegistry: Record<string, SceneBuilder> = {
    'basic/container': containerScene,
    'basic/container-pivot': containerPivotScene,
    'basic/blend-modes': blendModesScene,
    'basic/bitmap-text': bitmapTextScene,
    'basic/bitmap-text2': bitmapText2Scene,
    'basic/from-font': fromFontScene,
    'basic/pixi-text': pixiTextScene,
    'sprite/basic': basicSpriteScene,
    'sprite/animated-sprite': animatedSpriteScene,
    'sprite/tiling-sprite': tilingSpriteScene,
    'graphics/simple-graphics': simpleGraphicsScene,
    'filters/blur-filter': blurFilterScene,
    'masks/graphics-mask': graphicsMaskScene,
    'meshes/mesh-rope': meshRopeScene,
    'events/dragging': draggingScene,
    'textures/render-texture': renderTextureScene,
    'assets/asset-bundle': assetBundleScene,
    'advanced/star-warp': starWarpScene,
    'ecs/sprites': ecsSpritesScene,
    'games/snake': snakeScene,
    'games/tetris': tetrisScene,
    'games/breakout': breakoutScene,
    'games/asteroids': asteroidsScene,
};
