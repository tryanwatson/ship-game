using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using ShipGame.Net;

namespace ShipGame.Client.Rendering;

/// <summary>
/// Under the lobby's status banner: the name field (type to change it; it's all a player sets before a run) and the
/// crew, each by name, with who's ready.
/// </summary>
public sealed class LobbyPanel
{
    private const float Width = 440f;
    private const float Padding = 14f;
    private const float FieldHeight = 34f;
    private const float RowHeight = 22f;

    private static readonly Color Back = new Color(12, 16, 24) * 0.85f;
    private static readonly Color Border = new(110, 95, 70);
    private static readonly Color FieldBack = new(6, 8, 14);
    private static readonly Color FieldBorder = new(255, 215, 110);
    private static readonly Color Label = new(150, 155, 170);
    private static readonly Color Text = new(240, 215, 150);
    private static readonly Color Crew = new(225, 228, 235);
    private static readonly Color Ready = new(120, 230, 140);
    private static readonly Color Unnamed = new(120, 124, 136);

    private readonly PrimitiveBatch _batch;

    public LobbyPanel(PrimitiveBatch batch)
    {
        _batch = batch;
    }

    /// <param name="name">What's typed in the name field (not yet necessarily what the server has).</param>
    public void Draw(string name, IReadOnlyList<LobbyPlayer> players, int localPlayerId, HudView hud)
    {
        var viewport = hud.Viewport;
        var left = (viewport.Width - Width) / 2f;
        var top = viewport.Height * 0.3f + 110f;
        var height = Padding * 3 + PixelFont.Height(1.5f) + 6f + FieldHeight + players.Count * RowHeight + 10f;

        _batch.Begin(hud.Transform);
        Fill(left, top, Width, height, Back);
        Outline(left, top, Width, height, Border);

        var y = top + Padding;
        PixelFont.Draw(_batch, "YOUR NAME", new Vector2(left + Padding, y), 1.5f, Label);
        y += PixelFont.Height(1.5f) + 6f;
        Fill(left + Padding, y, Width - 2 * Padding, FieldHeight, FieldBack);
        Outline(left + Padding, y, Width - 2 * Padding, FieldHeight, FieldBorder);
        var caret = Environment.TickCount64 / 500 % 2 == 0 ? "_" : " ";
        PixelFont.Draw(_batch, name + caret, new Vector2(left + Padding + 10f, y + (FieldHeight - PixelFont.Height(2.5f)) / 2f), 2.5f, Text);
        y += FieldHeight + Padding;

        foreach (var player in players)
        {
            var you = player.PlayerId == localPlayerId ? " - YOU" : "";
            var named = player.Name.Length > 0;
            PixelFont.Draw(_batch, (named ? player.Name : $"SAILOR {player.PlayerId}") + you, new Vector2(left + Padding, y), 2f,
                named ? Crew : Unnamed);
            var state = player.Ready ? "READY" : named ? "..." : "NO NAME";
            PixelFont.Draw(_batch, state, new Vector2(left + Width - Padding - PixelFont.Measure(state, 2f), y), 2f,
                player.Ready ? Ready : Unnamed);
            y += RowHeight;
        }
        _batch.Flush();
    }

    private void Fill(float x, float y, float width, float height, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height) };
        _batch.FillConvex(rect, color);
    }

    private void Outline(float x, float y, float width, float height, Color color)
    {
        Span<Vector2> rect = stackalloc Vector2[] { new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height) };
        _batch.Outline(rect, color);
    }
}
