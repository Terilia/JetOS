// TERRAIN MAP DIAGNOSTIC - Paste this into a SEPARATE programmable block in-game.
// Renders a large terrain heightmap on an LCD named "LCD Map" (or PB surface 0).
// Requires the TerrainAPI Torch plugin to be loaded on the server.
// Run with UpdateFrequency.Update10 for auto-refresh, or click "Run" manually.
//
// This file is excluded from the MDK build (see csproj).

const int GRID_W = 32;
const int GRID_H = 32;
const int CELL_SIZE = 100; // meters per cell — 32*100 = 3.2km view

IMyTextSurface _lcd;
StringBuilder _req = new StringBuilder(512);
int[] _heights = new int[GRID_W * GRID_H];
bool _apiAvailable = true;
string _status = "Wait first scan...";

public Program()
{
    // Try to find dedicated LCD, fall back to PB surface 0
    var lcdBlock = GridTerminalSystem.GetBlockWithName("LCD Map") as IMyTextSurfaceProvider;
    if (lcdBlock != null)
        _lcd = lcdBlock.GetSurface(0);
    else if (Me is IMyTextSurfaceProvider)
        _lcd = ((IMyTextSurfaceProvider)Me).GetSurface(0);

    if (_lcd != null)
    {
        _lcd.ContentType = ContentType.SCRIPT;
        _lcd.Script = "";
        _lcd.ScriptBackgroundColor = new Color(5, 8, 5);
    }

    Runtime.UpdateFrequency = UpdateFrequency.Update10;
}

public void Main(string argument, UpdateType updateSource)
{
    // Probe once
    if (_apiAvailable && Me.GetProperty("TerrainAPI_Scan") == null)
    {
        _apiAvailable = false;
        _status = "TerrainAPI plugin loaded?";
        Echo(_status);
        DrawStatus();
        return;
    }

    if (!_apiAvailable)
    {
        Echo(_status);
        DrawStatus();
        return;
    }

    // Get position and forward from the grid
    var cockpit = FindCockpit();
    Vector3D pos;
    Vector3D fwd;
    if (cockpit != null)
    {
        pos = cockpit.GetPosition();
        fwd = cockpit.WorldMatrix.Forward;
        // Project forward onto gravity plane (yaw only)
        var grav = cockpit.GetNaturalGravity();
        if (grav.LengthSquared() > 0.01)
        {
            var up = Vector3D.Normalize(-grav);
            fwd = fwd - Vector3D.Dot(fwd, up) * up;
            if (fwd.LengthSquared() > 0.01)
                fwd = Vector3D.Normalize(fwd);
        }
    }
    else
    {
        pos = Me.CubeGrid.WorldVolume.Center;
        fwd = Me.WorldMatrix.Forward;
    }

    // Build request
    _req.Clear();
    _req.Append(pos.X).Append(';').Append(pos.Y).Append(';').Append(pos.Z).Append(';')
        .Append(fwd.X).Append(';').Append(fwd.Y).Append(';').Append(fwd.Z).Append(';')
        .Append(GRID_W).Append(';').Append(GRID_H).Append(';').Append(CELL_SIZE);

    Echo($"REQ: {_req.ToString().Substring(0, Math.Min(80, _req.Length))}...");

    try
    {
        Me.SetValue<StringBuilder>("TerrainAPI_Scan", _req);
        var resp = Me.GetValue<StringBuilder>("TerrainAPI_Scan");

        if (resp == null || resp.Length == 0)
        {
            _status = "Resp is null";
            Echo(_status);
            DrawStatus();
            return;
        }

        string respStr = resp.ToString();
        Echo($"RESP len={respStr.Length}");
        Echo($"RESP head: {respStr.Substring(0, Math.Min(80, respStr.Length))}");

        // Parse header: "OK;W;H;baseAlt;shipAlt;deltas..."
        int semiCount = 0;
        int dataStart = -1;
        for (int i = 0; i < respStr.Length; i++)
        {
            if (respStr[i] == ';')
            {
                semiCount++;
                if (semiCount == 5) { dataStart = i + 1; break; }
            }
        }

        if (dataStart < 0)
        {
            _status = $"Bad header — only {semiCount} semicolons found";
            Echo(_status);
            DrawStatus();
            return;
        }

        string header = respStr.Substring(0, dataStart - 1);
        string[] hp = header.Split(';');
        Echo($"Header parts: {hp.Length} => [{string.Join("|", hp)}]");

        if (hp.Length < 5 || hp[0] != "OK")
        {
            _status = $"Bad header: '{hp[0]}' (expected 'OK')";
            Echo(_status);
            DrawStatus();
            return;
        }

        int w = int.Parse(hp[1]);
        int h = int.Parse(hp[2]);
        double baseAlt = double.Parse(hp[3]);
        double shipAlt = double.Parse(hp[4]);

        Echo($"Grid: {w}x{h}, baseAlt={baseAlt:F0}, shipAlt={shipAlt:F0}");

        // Parse height deltas
        int totalCells = w * h;
        if (_heights.Length < totalCells)
            _heights = new int[totalCells];

        int idx = 0;
        int numStart = dataStart;
        for (int i = dataStart; i <= respStr.Length && idx < totalCells; i++)
        {
            if (i == respStr.Length || respStr[i] == ',')
            {
                if (i > numStart)
                {
                    int val = 0;
                    bool neg = false;
                    for (int c = numStart; c < i; c++)
                    {
                        char ch = respStr[c];
                        if (ch == '-') neg = true;
                        else if (ch >= '0' && ch <= '9') val = val * 10 + (ch - '0');
                    }
                    _heights[idx++] = neg ? -val : val;
                }
                numStart = i + 1;
            }
        }

        Echo($"Parsed {idx}/{totalCells} cells");
        _status = $"OK {w}x{h} | base={baseAlt:F0} ship={shipAlt:F0} | {idx} cells";

        if (idx >= totalCells)
            DrawMap(w, h, baseAlt, shipAlt);
        else
            DrawStatus();
    }
    catch (Exception ex)
    {
        _status = $"EXCEPTION: {ex.Message}";
        Echo(_status);
        _apiAvailable = false;
        DrawStatus();
    }
}

IMyCockpit FindCockpit()
{
    var blocks = new List<IMyCockpit>();
    GridTerminalSystem.GetBlocksOfType(blocks, b => b.IsSameConstructAs(Me));
    return blocks.Count > 0 ? blocks[0] : null;
}

void DrawStatus()
{
    if (_lcd == null) return;
    var frame = _lcd.DrawFrame();
    float sw = _lcd.SurfaceSize.X;
    float sh = _lcd.SurfaceSize.Y;

    // Background
    frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(sw / 2, sh / 2),
        new Vector2(sw, sh), new Color(5, 8, 5)));

    // Status text
    frame.Add(new MySprite(SpriteType.TEXT, _status, new Vector2(sw / 2, sh / 2 - 12),
        null, Color.Yellow, "Monospace", TextAlignment.CENTER, 0.6f));

    frame.Dispose();
}

void DrawMap(int w, int h, double baseAlt, double shipAlt)
{
    if (_lcd == null) return;
    var frame = _lcd.DrawFrame();
    float sw = _lcd.SurfaceSize.X;
    float sh = _lcd.SurfaceSize.Y;

    // Background
    frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(sw / 2, sh / 2),
        new Vector2(sw, sh), new Color(5, 8, 5)));

    // Title
    frame.Add(new MySprite(SpriteType.TEXT, $"TERRAIN MAP  {GRID_W}x{GRID_H} @ {CELL_SIZE}m",
        new Vector2(sw / 2, 4), null, new Color(138, 122, 80), "Monospace", TextAlignment.CENTER, 0.5f));

    // Grid layout — square, centered, leave room for title/footer
    float margin = 30f;
    float gridAvail = Math.Min(sw - margin * 2, sh - margin * 2 - 40f);
    float cellPx = gridAvail / Math.Max(w, h);
    float gridLeft = (sw - w * cellPx) / 2f;
    float gridTop = margin + 20f;

    // Row-batched rendering
    for (int z = 0; z < h; z++)
    {
        int runStart = 0;
        Color runColor = ClearColor(shipAlt - (baseAlt + _heights[z * w]));

        for (int x = 1; x <= w; x++)
        {
            Color thisColor = Color.Transparent;
            if (x < w)
                thisColor = ClearColor(shipAlt - (baseAlt + _heights[z * w + x]));

            if (x == w || thisColor != runColor)
            {
                int runLen = x - runStart;
                float bx = gridLeft + runStart * cellPx + runLen * cellPx / 2f;
                float by = gridTop + z * cellPx + cellPx / 2f;
                frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple",
                    new Vector2(bx, by), new Vector2(runLen * cellPx, cellPx),
                    runColor, alignment: TextAlignment.CENTER));

                runStart = x;
                runColor = thisColor;
            }
        }
    }

    // Ship marker
    float cx = gridLeft + (w * cellPx) / 2f;
    float cy = gridTop + (h * cellPx) / 2f;
    frame.Add(new MySprite(SpriteType.TEXTURE, "Triangle",
        new Vector2(cx, cy), new Vector2(10, 10),
        new Color(144, 208, 144), alignment: TextAlignment.CENTER));

    // Grid outline
    float gw = w * cellPx;
    float gh = h * cellPx;
    frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(gridLeft + gw / 2, gridTop), new Vector2(gw, 1), new Color(24, 40, 24), alignment: TextAlignment.CENTER));
    frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(gridLeft + gw / 2, gridTop + gh), new Vector2(gw, 1), new Color(24, 40, 24), alignment: TextAlignment.CENTER));
    frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(gridLeft, gridTop + gh / 2), new Vector2(1, gh), new Color(24, 40, 24), alignment: TextAlignment.CENTER));
    frame.Add(new MySprite(SpriteType.TEXTURE, "SquareSimple", new Vector2(gridLeft + gw, gridTop + gh / 2), new Vector2(1, gh), new Color(24, 40, 24), alignment: TextAlignment.CENTER));

    // Footer: AGL + view range
    int centerIdx = (h / 2) * w + w / 2;
    double agl = shipAlt - (baseAlt + _heights[centerIdx]);
    Color aglC = agl < 100 ? new Color(180, 50, 40) : agl < 200 ? new Color(160, 140, 40) : new Color(80, 144, 80);
    float footY = gridTop + gh + 8f;

    frame.Add(new MySprite(SpriteType.TEXT, $"AGL {agl:F0}m",
        new Vector2(sw / 2 - 60, footY), null, aglC, "Monospace", TextAlignment.LEFT, 0.5f));

    float viewKm = Math.Max(w, h) * CELL_SIZE / 1000f;
    frame.Add(new MySprite(SpriteType.TEXT, $"VIEW {viewKm:F1}km",
        new Vector2(sw / 2 + 60, footY), null, new Color(42, 74, 42), "Monospace", TextAlignment.RIGHT, 0.5f));

    frame.Dispose();
}

Color ClearColor(double clearance)
{
    if (clearance < 0) return new Color(180, 50, 40);
    if (clearance < 50) return new Color(160, 140, 40);
    if (clearance < 150) return new Color(64, 140, 48);
    if (clearance < 400) return new Color(32, 80, 32);
    if (clearance < 800) return new Color(12, 40, 12);
    return new Color(4, 12, 4);
}
