using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        abstract class UIElement
        {
            public Vector2 Position { get; set; }
            public float Scale { get; set; } = 1f;
            public Color TextColor { get; set; } = Color.Green;
            public UIElement(Vector2 position)
            {
                Position = position;
            }
            public abstract void Draw(ref MySpriteDrawFrame frame, RectangleF viewport);
        }

        class UILabel : UIElement
        {
            public string Text { get; set; }
            public bool FixedWidth { get; set; } = true; // Default to military precision
            public UILabel(string text, Vector2 position) : base(position)
            {
                Text = text;
            }
            public override void Draw(ref MySpriteDrawFrame frame, RectangleF viewport)
            {
                var lines = Text.Split('\n');
                float lineHeight = 20f * Scale;
                Vector2 startPos = Position + viewport.Position;
                for (int i = 0; i < lines.Length; i++)
                {
                    var textSprite = new MySprite()
                    {
                        Type = SpriteType.TEXT,
                        Data = lines[i],
                        Position = startPos + new Vector2(0, i * lineHeight),
                        RotationOrScale = Scale,
                        Color = TextColor,
                        Alignment = TextAlignment.LEFT,
                        FontId = FixedWidth ? "Monospace" : "White"
                    };
                    frame.Add(textSprite);
                }
            }
        }

        class UIContainer : UIElement
        {
            public Vector2 Size { get; set; }
            public List<UIElement> Elements { get; } = new List<UIElement>();
            public Color BorderColor { get; set; } = Color.Green;
            public float BorderThickness { get; set; } = 2f;
            public Vector2 Padding { get; set; } = new Vector2(10f, 10f);

            public UIContainer(Vector2 position, Vector2 size) : base(position)
            {
                Size = size;
            }

            public UIContainer AddElement(UIElement element)
            {
                element.Position += Position + Padding;
                Elements.Add(element);
                return this;
            }

            public override void Draw(ref MySpriteDrawFrame frame, RectangleF viewport)
            {
                DrawBorder(ref frame, viewport);
                foreach (var element in Elements)
                {
                    element.Draw(ref frame, viewport);
                }
            }

            private void DrawBorder(ref MySpriteDrawFrame frame, RectangleF viewport)
            {
                var topLeft = Position + viewport.Position;
                var outerRect = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareHollow",
                    Position = topLeft + Size / 2,
                    Size = Size,
                    Color = BorderColor,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(outerRect);

                var glowingEffect = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = topLeft + Size / 2,
                    Size = Size - new Vector2(BorderThickness, BorderThickness),
                    Color = BorderColor * 0.5f,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(glowingEffect);
            }
        }

        class UISquare : UIElement
        {
            public Vector2 Size { get; set; }
            public Color FillColor { get; set; } = new Color(0, 50, 0); // Dark green for a military look
            public UISquare(Vector2 position, Vector2 size, Color fillColor) : base(position)
            {
                Size = size;
                FillColor = fillColor;
            }

            public override void Draw(ref MySpriteDrawFrame frame, RectangleF viewport)
            {
                var square = new MySprite()
                {
                    Type = SpriteType.TEXTURE,
                    Data = "SquareSimple",
                    Position = Position + viewport.Position + Size / 2,
                    Size = Size,
                    Color = FillColor,
                    Alignment = TextAlignment.CENTER
                };
                frame.Add(square);
            }
        }
    }
}
