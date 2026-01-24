using Sandbox.ModAPI.Ingame;
using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class UIController
        {
            private static readonly Color BORDER_COLOR = new Color(30, 50, 30);
            private static readonly Color BACKGROUND_COLOR = new Color(10, 15, 10);
            private static readonly Color TITLE_COLOR = new Color(100, 150, 100);
            private static readonly Color TEXT_COLOR = new Color(200, 200, 180);
            private static readonly Color HIGHLIGHT_COLOR = new Color(150, 100, 50);
            private static readonly Color NAVIGATION_COLOR = new Color(80, 120, 80);
            private static readonly Color BLACK_BACKGROUND = new Color(0, 0, 0);
            private static readonly Color TITLE_BACKGROUND = new Color(20, 30, 20);
            private const int MAIN_VIEWPORT_HEIGHT = 0;
            private const int CONTENT_PADDING_TOP = 30;
            private const int OPTION_HEIGHT = 23;
            private const int NAVIGATION_INSTRUCTIONS_HEIGHT = 40;
            private const int EXTRA_VIEWPORT_HEIGHT = 40;
            private const int BORDER_THICKNESS = 2;
            private const int PADDING_TOP = 10;
            private const int PADDING_BOTTOM = 10;
            private const float TITLE_SCALE = 0.6f;
            private const float OPTION_SCALE = 0.6f;
            private const float NAVIGATION_SCALE = 0.6f;
            private IMyTextSurface mainScreen;
            private IMyTextSurface extraScreen;
            private RectangleF mainViewport;
            private RectangleF extraViewport;
            private List<UIElement> mainElements = new List<UIElement>();
            private List<UIElement> extraElements = new List<UIElement>();
            public IMyTextSurface MainScreen => mainScreen;
            public IMyTextSurface ExtraScreen => extraScreen;
            public UIController(IMyTextSurface mainScreen, IMyTextSurface extraScreen)
            {
                this.mainScreen = mainScreen;
                this.extraScreen = extraScreen;
                PrepareTextSurfaceForSprites(mainScreen);
                PrepareTextSurfaceForSprites(extraScreen);
                mainViewport = new RectangleF(Vector2.Zero, mainScreen.SurfaceSize);
                extraViewport = new RectangleF(Vector2.Zero, extraScreen.SurfaceSize);

                InitializeUI();
            }
            public void RenderCustomFrame(
                Action<MySpriteDrawFrame, RectangleF> customRender,
                RectangleF area
            )
            {
                var frame = mainScreen.DrawFrame();
                customRender?.Invoke(frame, area);
                frame.Dispose();
            }
            public void RenderCustomExtraFrame(
                Action<MySpriteDrawFrame, RectangleF> customRender,
                RectangleF area
            )
            {
                var frame = extraScreen.DrawFrame();
                customRender?.Invoke(frame, area);
                frame.Dispose();
            }
            public void RenderMainScreen(
                string title,
                string[] options,
                int currentMenuIndex,
                string navigationInstructions,
                int scrollOffset = 0
            )
            {
                var frame = mainScreen.DrawFrame();
                mainElements.Clear();

                // Draw the main background
                DrawBackground(frame, mainViewport, BLACK_BACKGROUND);

                // Add extra padding for the title
                float titlePaddingTop = 20f;
                DrawBackground(
                    frame,
                    new RectangleF(
                        new Vector2(0, titlePaddingTop),
                        new Vector2(mainViewport.Width, MAIN_VIEWPORT_HEIGHT)
                    ),
                    TITLE_BACKGROUND
                );

                // Create title container with added padding
                mainElements.Add(
                    new UIContainer(
                        new Vector2(0, titlePaddingTop),
                        new Vector2(mainViewport.Width, MAIN_VIEWPORT_HEIGHT)
                    )
                    {
                        BorderColor = BORDER_COLOR,
                        BorderThickness = BORDER_THICKNESS,
                        Padding = new Vector2(PADDING_TOP, PADDING_BOTTOM)
                    }.AddElement(
                        new UILabel(title, Vector2.Zero)
                        {
                            Scale = TITLE_SCALE,
                            TextColor = TITLE_COLOR
                        }
                    )
                );

                // Calculate available height for content area
                float availableContentHeight = mainViewport.Height - CONTENT_PADDING_TOP - titlePaddingTop - NAVIGATION_INSTRUCTIONS_HEIGHT - 60;

                // Calculate individual option heights
                float[] optionHeights = new float[options.Length];
                for (int i = 0; i < options.Length; i++)
                {
                    int lineCount = options[i].Split(new[] { '\n' }, StringSplitOptions.None).Length;
                    optionHeights[i] = lineCount * OPTION_HEIGHT * OPTION_SCALE;
                }

                // Position content with padding
                var contentPosition = new Vector2(0, CONTENT_PADDING_TOP + titlePaddingTop);
                var contentSize = new Vector2(mainViewport.Width, availableContentHeight);
                var container = new UIContainer(contentPosition, contentSize)
                {
                    BorderColor = BORDER_COLOR,
                    BorderThickness = BORDER_THICKNESS,
                    Padding = new Vector2(PADDING_TOP, 5)
                };

                // Add options to the container with scrolling support
                float currentY = -scrollOffset * OPTION_HEIGHT * OPTION_SCALE;
                for (int i = 0; i < options.Length; i++)
                {
                    string option = options[i];
                    int lineCount = option.Split(new[] { '\n' }, StringSplitOptions.None).Length;
                    float optionHeight = lineCount * OPTION_HEIGHT;

                    // Only render options that are visible in the viewport
                    if (currentY + optionHeights[i] >= -10 && currentY <= availableContentHeight + 10)
                    {
                        var optionText = new UILabel(option, new Vector2(20, currentY))
                        {
                            Scale = OPTION_SCALE,
                            TextColor = TEXT_COLOR
                        };

                        // Highlight the current option
                        if (i == currentMenuIndex)
                        {
                            AddArrowIndicator(
                                container,
                                new Vector2(5, currentY + optionHeight / 2 - 5)
                            );
                        }

                        container.AddElement(optionText);
                    }
                    currentY += optionHeights[i];
                }

                // Add the content container
                mainElements.Add(container);

                // Draw navigation instructions
                mainElements.Add(
                    new UIContainer(
                        new Vector2(0, contentSize.Y + titlePaddingTop + 60),
                        new Vector2(mainViewport.Width, NAVIGATION_INSTRUCTIONS_HEIGHT)
                    )
                    {
                        BorderColor = BORDER_COLOR,
                        BorderThickness = BORDER_THICKNESS,
                        Padding = new Vector2(PADDING_TOP, PADDING_BOTTOM)
                    }.AddElement(
                        new UILabel(navigationInstructions, Vector2.Zero)
                        {
                            Scale = NAVIGATION_SCALE,
                            TextColor = NAVIGATION_COLOR
                        }
                    )
                );

                // Draw all elements
                foreach (var element in mainElements)
                {
                    element.Draw(ref frame, mainViewport);
                }

                frame.Dispose();
            }

            public void RenderExtraScreen(string title, string content)
            {
                var frame = extraScreen.DrawFrame();
                extraElements.Clear();
                DrawBackground(frame, extraViewport, BLACK_BACKGROUND);
                DrawBackground(
                    frame,
                    new RectangleF(
                        new Vector2(0, 0),
                        new Vector2(extraViewport.Width, EXTRA_VIEWPORT_HEIGHT)
                    ),
                    TITLE_BACKGROUND
                );
                extraElements.Add(
                    new UIContainer(
                        new Vector2(0, 0),
                        new Vector2(extraViewport.Width, EXTRA_VIEWPORT_HEIGHT)
                    )
                    {
                        BorderColor = BORDER_COLOR,
                        BorderThickness = BORDER_THICKNESS,
                        Padding = new Vector2(PADDING_TOP, PADDING_BOTTOM)
                    }.AddElement(
                        new UILabel(title, Vector2.Zero)
                        {
                            Scale = TITLE_SCALE,
                            TextColor = TITLE_COLOR
                        }
                    )
                );
                int lineCount = content.Split(new[] { '\n' }, StringSplitOptions.None).Length;
                float lineHeight = OPTION_HEIGHT * OPTION_SCALE;
                float contentHeight = (lineCount * lineHeight) + (PADDING_TOP + PADDING_BOTTOM);
                contentHeight = Math.Max(contentHeight, EXTRA_VIEWPORT_HEIGHT + 10);
                extraElements.Add(
                    new UIContainer(
                        new Vector2(0, EXTRA_VIEWPORT_HEIGHT + 10),
                        new Vector2(extraViewport.Width, contentHeight)
                    )
                    {
                        BorderColor = BORDER_COLOR,
                        BorderThickness = BORDER_THICKNESS,
                        Padding = new Vector2(PADDING_TOP, PADDING_BOTTOM)
                    }.AddElement(
                        new UILabel(content, Vector2.Zero)
                        {
                            Scale = OPTION_SCALE,
                            TextColor = TEXT_COLOR,
                            FixedWidth = true
                        }
                    )
                );
                foreach (var element in extraElements)
                {
                    element.Draw(ref frame, extraViewport);
                }
                frame.Dispose();
            }
            private void AddArrowIndicator(UIContainer container, Vector2 position)
            {
                var arrowSprite = new MySprite
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = position + new Vector2(7, 0),
                    Size = new Vector2(10, 10),
                    Color = HIGHLIGHT_COLOR,
                    Alignment = TextAlignment.CENTER
                };
                container.AddElement(new UISquare(position, new Vector2(10, 10), HIGHLIGHT_COLOR));
            }
            private void DrawBackground(MySpriteDrawFrame frame, RectangleF area, Color color)
            {
                var backgroundSprite = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = area.Position + area.Size / 2,
                    Size = area.Size,
                    Color = color,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(backgroundSprite);
            }
            private void PrepareTextSurfaceForSprites(IMyTextSurface textSurface)
            {
                textSurface.ContentType = ContentType.SCRIPT;
                textSurface.Script = ""; // Ensure no other script is running
                textSurface.BackgroundColor = Color.Transparent; // Set to transparent if possible
                textSurface.FontColor = Color.Black; // Ensure font color is not causing any issues
                textSurface.FontSize = 0.1f; // Minimal font size to reduce impact
                textSurface.TextPadding = 0f; // No padding
                textSurface.Alignment = TextAlignment.CENTER;
            }

            private void InitializeUI()
            {
                mainScreen.BackgroundColor = BLACK_BACKGROUND;
                extraScreen.BackgroundColor = BLACK_BACKGROUND;
            }
        }
    }
}
