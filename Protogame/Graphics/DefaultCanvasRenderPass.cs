﻿using System;
using Microsoft.Xna.Framework.Graphics;

namespace Protogame
{
    /// <summary>
    /// The default implementation of an <see cref="ICanvasRenderPass"/>.
    /// </summary>
    /// <module>Graphics</module>
    /// <internal>True</internal>
    /// <interface_ref>Protogame.ICanvasRenderPass</interface_ref>
    public class DefaultCanvasRenderPass : ICanvasRenderPass
    {
        private Viewport _viewport;

        private bool _viewportConfigured;

        public DefaultCanvasRenderPass()
        {
        }
        
        public bool IsPostProcessingPass => false;
        public bool SkipWorldRenderBelow => false;
        public bool SkipWorldRenderAbove => false;
        public bool SkipEntityRender => false;
        public bool SkipEngineHookRender => false;
        public string EffectTechniqueName => RenderPipelineTechniqueName.Canvas;

        public Viewport Viewport
        {
            get
            {
                return _viewport;
            }
            set
            {
                _viewport = value;
                _viewportConfigured = true;
            }
        }

        public SpriteSortMode TextureSortMode
        {
            get;
            set;
        }

        public void BeginRenderPass(IGameContext gameContext, IRenderContext renderContext, IRenderPass previousPass, RenderTarget2D postProcessingSource)
        {
#if PLATFORM_WINDOWS
            if (!_viewportConfigured)
            {
                _viewport = new Viewport(
                    0,
                    0,
                    gameContext.Game.Window.ClientBounds.Width,
                    gameContext.Game.Window.ClientBounds.Height);
                _viewportConfigured = true;
            }
#endif


            if (_viewportConfigured)
            {
                renderContext.GraphicsDevice.Viewport = this.Viewport;
            }

            renderContext.Is3DContext = false;

            renderContext.SpriteBatch.Begin(TextureSortMode);
        }

        public void EndRenderPass(IGameContext gameContext, IRenderContext renderContext, IRenderPass nextPass)
        {
            renderContext.SpriteBatch.End();
        }
    }
}

