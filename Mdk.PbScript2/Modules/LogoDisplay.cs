using System;
using System.Collections.Generic;
using VRage.Game.GUI.TextPanel;
using VRageMath;

namespace IngameScript
{
    partial class Program
    {
        class LogoDisplay : ProgramModule
        {
            private UIController uiController;
            private bool isActive = false;
            private List<Particle> particles;
            private List<DustParticle> dustParticles; // List for dust particles
            private Random random = new Random();
            private const int particleCount = 49;
            private const int dustParticleCount = 120; // Number of dust particles
            private const float particleSpeed = 0.2f;
            private const float dustSpeed = 0.4f; // Slower speed for dust particles
            private const float wobbleIntensity = 0.1f;
            private const float minParticleSize = 2f;
            private const float maxParticleSize = 12f;
            private const float minDustSize = 1f; // Smaller size for dust particles
            private const float maxDustSize = 7f;
            private const float minOpacity = 0.3f;
            private const float maxOpacity = 0.8f;
            private const float linkDistance = 80;
            private int motivationalIndex = 0;
            private int currentEvilIndex = 0;
            private int tickCounter = 0;
            private int animationcounter = 0;
            private int ticksPerMotivational = 400;
            private int ticksPerEvil = 200;
            private bool showingEvilText = false;
            private List<TrailParticle> trailParticles = new List<TrailParticle>();




            private List<string> motivationalTexts = new List<string>
            {
                "Innovate for a better\ntomorrow",
                "Success is a journey,\nnot a destination",
                "Every challenge is\nan opportunity",
                "Strive for progress,\nnot perfection",
                "Commit to your goals\nand achieve greatness",
                "Lead with courage,\nact with integrity"
            };
            private List<string> evilTexts = new List<string>
            {
                "We thrive on\nbribery and power",
                "Control is profit,\nand profit is control",
                "Peace is a myth;\nconflict pays well",
                "Infiltrate and dominate",
                "Power is the ultimate\ncurrency",
                "Chaos breeds opportunity",
                "Fear is the greatest\ntool of control",
                "Silence dissent\nthrough intimidation",
                "Corruption is the price\nof ultimate control"
            };

            public bool IsActive
            {
                get { return isActive; }
            }

            public LogoDisplay(Program program, UIController uiController) : base(program)
            {
                name = "ScreenSaver";
                this.uiController = uiController;
                particles = new List<Particle>();
                dustParticles = new List<DustParticle>();

                List<string> snowflakeTextures = new List<string>
                {
                    "Snowflake1",
                    "Snowflake2",
                    "Snowflake3"
                };
                List<Color> snowflakeColors = new List<Color>
                {
                    Color.White,
                    Color.LightBlue,
                    Color.Cyan
                };

                // Initialize snowflake particles
                for (int i = 0; i < particleCount; i++)
                {
                    particles.Add(
                        new Particle(
                            new Vector2(
                                random.Next(0, (int)uiController.MainScreen.SurfaceSize.X),
                                random.Next(0, (int)uiController.MainScreen.SurfaceSize.Y)
                            ),
                            new Vector2(0f, (float)(random.NextDouble() * 0.5 + 0.5))
                                * particleSpeed,
                            (float)random.NextDouble() * (maxOpacity - minOpacity) + minOpacity,
                            (float)random.NextDouble() * (maxParticleSize - minParticleSize)
                                + minParticleSize,
                            snowflakeColors[random.Next(snowflakeColors.Count)],
                            snowflakeTextures[random.Next(snowflakeTextures.Count)],
                            random
                        )
                    );
                }

                // Initialize falling snow dust particles
                for (int i = 0; i < dustParticleCount; i++)
                {
                    dustParticles.Add(
                        new DustParticle(
                            new Vector2(
                                random.Next(0, (int)uiController.MainScreen.SurfaceSize.X),
                                random.Next(0, (int)uiController.MainScreen.SurfaceSize.Y)
                            ),
                            new Vector2(0f, (float)(random.NextDouble() * 0.5 + 0.5)) * dustSpeed,
                            (float)random.NextDouble() * (maxOpacity - minOpacity) + minOpacity,
                            (float)random.NextDouble() * (maxDustSize - minDustSize) + minDustSize,
                            Color.White
                        )
                    );
                }
            }

            public override string[] GetOptions() =>
                new string[] { "Display Christmas Animation", "Back" };

            public override void ExecuteOption(int index)
            {
                switch (index)
                {
                    case 0:
                        isActive = true;
                        break;
                    case 1:
                        isActive = false;
                        SystemManager.ReturnToMainMenu();
                        break;
                }
            }

            public override void HandleSpecialFunction(int key)
            {
                if (isActive)
                {
                    isActive = false;
                    SystemManager.ReturnToMainMenu();
                }
            }

            public override void Tick()
            {
                if (isActive)
                {
                    //UpdateParticles();
                    animationcounter++;
                    Vector2 screenSize = uiController.MainScreen.SurfaceSize;
                    uiController.RenderCustomFrame(
                        (frame, area) => RenderParticles(frame, area),
                        new RectangleF(Vector2.Zero, screenSize)
                    );
                    tickCounter++;
                    if (showingEvilText && tickCounter >= ticksPerEvil)
                    {
                        showingEvilText = false;
                        tickCounter = 0;
                    }
                    else if (!showingEvilText && tickCounter >= ticksPerMotivational)
                    {
                        if (random.Next(10) < 1)
                        {
                            showingEvilText = true;
                            currentEvilIndex = random.Next(evilTexts.Count);
                            motivationalIndex = (motivationalIndex + 1) % motivationalTexts.Count;
                        }
                        else
                        {
                            motivationalIndex = (motivationalIndex + 1) % motivationalTexts.Count;
                        }
                        tickCounter = 0;
                    }
                }
            }

            private float NextFloat(float min, float max)
            {
                return (float)(random.NextDouble() * (max - min) + min);
            }

            private void UpdateParticles()
            {
                Vector2 screenSize = uiController.MainScreen.SurfaceSize;

                // Update snowflake particles
                for (int i = 0; i < particles.Count; i++)
                {
                    Particle particle = particles[i];
                    particle.Position += particle.Velocity;

                    if (particle.Position.Y > screenSize.Y)
                    {
                        particle.Position.Y = 0;
                        particle.Position.X = random.Next(0, (int)screenSize.X);
                    }

                    particles[i] = particle;
                }

                // Update dust particles
                for (int i = 0; i < dustParticles.Count; i++)
                {
                    var dust = dustParticles[i];
                    dust.Position += dust.Velocity;

                    if (dust.Position.Y > screenSize.Y)
                    {
                        dust.Position.Y = 0;
                        dust.Position.X = random.Next(0, (int)screenSize.X);
                    }

                    dustParticles[i] = dust;
                }
            }

            // OPTIMIZED: Replaced expensive Mandelbrot rendering with simple animated text
            private void RenderParticles(MySpriteDrawFrame frame, RectangleF area)
            {
                float time = animationcounter / 60.0f;
                Vector2 resolution = new Vector2(area.Width, area.Height);
                Vector2 center = resolution / 2.0f;

                // Draw animated "JetOS" logo
                string logoText = "JetOS";
                float logoScale = 3.0f + (float)Math.Sin(time * 2) * 0.3f; // Pulsing effect
                Color logoColor = new Color(
                    (int)(128 + 127 * Math.Sin(time)),
                    (int)(128 + 127 * Math.Sin(time + 2)),
                    (int)(128 + 127 * Math.Sin(time + 4))
                );

                var logoSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = logoText,
                    Position = center,
                    RotationOrScale = logoScale,
                    Color = logoColor,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                };
                frame.Add(logoSprite);

                // Draw some simple animated stars (much cheaper than Mandelbrot!)
                int starCount = 20;
                for (int i = 0; i < starCount; i++)
                {
                    float angle = (time + i) * 0.1f;
                    float radius = 100 + i * 15;
                    Vector2 starPos = center + new Vector2(
                        (float)Math.Cos(angle) * radius,
                        (float)Math.Sin(angle) * radius
                    );

                    var starSprite = new MySprite()
                    {
                        Type = SpriteType.TEXTURE,
                        Data = "Circle",
                        Position = starPos,
                        Size = new Vector2(2, 2),
                        Color = Color.White,
                        Alignment = TextAlignment.CENTER
                    };
                    frame.Add(starSprite);
                }

                // Show motivational text below logo
                int textIndex = (animationcounter / 240) % motivationalTexts.Count;
                string motivText = motivationalTexts[textIndex];

                var textSprite = new MySprite()
                {
                    Type = SpriteType.TEXT,
                    Data = motivText,
                    Position = center + new Vector2(0, 100),
                    RotationOrScale = 0.8f,
                    Color = Color.LightGray,
                    Alignment = TextAlignment.CENTER,
                    FontId = "White"
                };
                frame.Add(textSprite);
            }


            private struct Particle
            {
                public Vector2 Position;
                public Vector2 Velocity;
                public float Opacity;
                public float Size;
                public Color Color;
                public string Texture;

                public Particle(
                    Vector2 position,
                    Vector2 velocity,
                    float opacity,
                    float size,
                    Color color,
                    string texture,
                    Random random
                )
                {
                    Position = position;
                    Velocity = velocity;
                    Opacity = opacity;
                    Size = size;
                    Color = color;
                    Texture = texture;
                }
            }

            private struct DustParticle
            {
                public Vector2 Position;
                public Vector2 Velocity;
                public float Opacity;
                public float Size;
                public Color Color;

                public DustParticle(
                    Vector2 position,
                    Vector2 velocity,
                    float opacity,
                    float size,
                    Color color
                )
                {
                    Position = position;
                    Velocity = velocity;
                    Opacity = opacity;
                    Size = size;
                    Color = color;
                }
            }

            private struct TrailParticle
            {
                public Vector2 Position;
                public Vector2 Velocity;
                public float Opacity;
                public float Size;
                public Color Color;
                public float Lifetime;

                public TrailParticle(
                    Vector2 position,
                    Vector2 velocity,
                    float opacity,
                    float size,
                    float lifetime,
                    Color color
                )
                {
                    Position = position;
                    Velocity = velocity;
                    Opacity = opacity;
                    Size = size;
                    Lifetime = lifetime;
                    Color = color;
                }
            }
        }
    }
}
