using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using OmegaAssetStudio.MeshPreview;

namespace OmegaAssetStudio.WinUI.Rendering;

internal sealed class MeshPreviewSoftwareRenderer
{
    private static readonly Vector3 BackgroundTop = new(0.18f, 0.19f, 0.21f);
    private static readonly Vector3 BackgroundBottom = new(0.10f, 0.11f, 0.13f);
    private static readonly Vector3 ClayColor = new(0.88f, 0.88f, 0.86f);
    private static readonly Vector3 StudioGray = new(0.72f, 0.72f, 0.74f);
    private static readonly Vector3 LitColor = new(0.84f, 0.85f, 0.88f);
    private static readonly Vector3 MatCapColor = new(0.90f, 0.87f, 0.79f);
    private static readonly Vector3 GameApproxColor = new(0.86f, 0.82f, 0.76f);
    private static readonly Vector3 NeutralLightDirection = Vector3.Normalize(new Vector3(-0.45f, -0.35f, 0.82f));
    private static readonly Vector3 StudioLightDirection = Vector3.Normalize(new Vector3(-0.30f, -0.55f, 0.78f));
    private static readonly Vector3 HarshLightDirection = Vector3.Normalize(new Vector3(-0.15f, -0.10f, 0.98f));
    private static readonly Vector3 SoftLightDirection = Vector3.Normalize(new Vector3(-0.55f, -0.35f, 0.66f));

    public WriteableBitmap Render(
        MeshPreviewScene scene,
        int width,
        int height,
        MeshPreviewCamera camera,
        MeshPreviewShadingMode shadingMode,
        MeshPreviewBackgroundStyle backgroundStyle,
        MeshPreviewLightingPreset lightingPreset,
        bool wireframe,
        bool showGroundPlane)
    {
        int safeWidth = Math.Max(64, width);
        int safeHeight = Math.Max(64, height);

        WriteableBitmap bitmap = new(safeWidth, safeHeight);
        byte[] pixels = new byte[safeWidth * safeHeight * 4];
        float[] depth = new float[safeWidth * safeHeight];
        Array.Fill(depth, float.PositiveInfinity);

        FillBackground(pixels, safeWidth, safeHeight, backgroundStyle);

        bool renderSideBySide = scene.DisplayMode == MeshPreviewDisplayMode.SideBySide;
        bool drawFbx = scene.ShowFbxMesh && scene.FbxMesh is not null && scene.DisplayMode != MeshPreviewDisplayMode.Ue3Only;
        bool drawUe3 = scene.ShowUe3Mesh && scene.Ue3Mesh is not null && scene.DisplayMode != MeshPreviewDisplayMode.FbxOnly;

        if (!drawFbx && !drawUe3)
        {
            WritePixels(bitmap, pixels);
            return bitmap;
        }

        if (showGroundPlane)
            DrawGroundPlane(pixels, depth, safeWidth, safeHeight, camera.GetViewMatrix() * camera.GetProjectionMatrix(safeWidth / (float)safeHeight));

        if (renderSideBySide)
        {
            int leftWidth = Math.Max(1, safeWidth / 2);
            int rightWidth = Math.Max(1, safeWidth - leftWidth);
            if (drawFbx)
                RenderMesh(scene.FbxMesh!, pixels, depth, 0, 0, leftWidth, safeHeight, camera, shadingMode, lightingPreset, wireframe);
            if (drawUe3)
                RenderMesh(scene.Ue3Mesh!, pixels, depth, leftWidth, 0, rightWidth, safeHeight, camera, shadingMode, lightingPreset, wireframe);
        }
        else
        {
            if (drawFbx)
                RenderMesh(scene.FbxMesh!, pixels, depth, 0, 0, safeWidth, safeHeight, camera, shadingMode, lightingPreset, wireframe);
            if (drawUe3)
                RenderMesh(scene.Ue3Mesh!, pixels, depth, 0, 0, safeWidth, safeHeight, camera, shadingMode, lightingPreset, wireframe);
        }

        WritePixels(bitmap, pixels);
        return bitmap;
    }

    private static void RenderMesh(
        MeshPreviewMesh mesh,
        byte[] pixels,
        float[] depth,
        int viewportX,
        int viewportY,
        int viewportWidth,
        int viewportHeight,
        MeshPreviewCamera camera,
        MeshPreviewShadingMode shadingMode,
        MeshPreviewLightingPreset lightingPreset,
        bool wireframe)
    {
        if (mesh.Vertices.Count == 0 || mesh.Indices.Count < 3)
            return;

        float aspect = Math.Max(1, viewportWidth) / (float)Math.Max(1, viewportHeight);
        Matrix4x4 view = camera.GetViewMatrix();
        Matrix4x4 projection = camera.GetProjectionMatrix(aspect);
        Matrix4x4 viewProjection = view * projection;
        Vector3 lightDirection = GetLightDirection(lightingPreset);

        Vector3[] worldPositions = new Vector3[mesh.Vertices.Count];
        Vector3[] viewPositions = new Vector3[mesh.Vertices.Count];
        Vector2[] screenPositions = new Vector2[mesh.Vertices.Count];
        float[] ndcDepths = new float[mesh.Vertices.Count];
        bool[] visible = new bool[mesh.Vertices.Count];

        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            Vector3 position = mesh.Vertices[i].Position;
            worldPositions[i] = position;
            Vector3 viewPosition = Vector3.Transform(position, view);
            viewPositions[i] = viewPosition;

            Vector4 clip = Vector4.Transform(new Vector4(position, 1.0f), viewProjection);
            if (clip.W <= 0.0001f)
                continue;

            Vector3 ndc = new(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
            visible[i] = ndc.X is >= -1.5f and <= 1.5f && ndc.Y is >= -1.5f and <= 1.5f;
            ndcDepths[i] = ndc.Z;
            screenPositions[i] = new Vector2(
                viewportX + ((ndc.X * 0.5f + 0.5f) * Math.Max(1, viewportWidth - 1)),
                viewportY + ((1.0f - (ndc.Y * 0.5f + 0.5f)) * Math.Max(1, viewportHeight - 1)));
        }

        for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
        {
            int i0 = (int)mesh.Indices[i];
            int i1 = (int)mesh.Indices[i + 1];
            int i2 = (int)mesh.Indices[i + 2];
            if ((uint)i0 >= mesh.Vertices.Count || (uint)i1 >= mesh.Vertices.Count || (uint)i2 >= mesh.Vertices.Count)
                continue;

            if (!visible[i0] && !visible[i1] && !visible[i2])
                continue;

            Vector3 p0 = worldPositions[i0];
            Vector3 p1 = worldPositions[i1];
            Vector3 p2 = worldPositions[i2];
            Vector3 normal = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));
            if (float.IsNaN(normal.X))
                continue;

            Vector3 v0 = viewPositions[i0];
            Vector3 v1 = viewPositions[i1];
            Vector3 v2 = viewPositions[i2];
            Vector3 viewNormal = Vector3.Normalize(Vector3.Cross(v1 - v0, v2 - v0));
            if (float.IsNaN(viewNormal.Z))
                continue;

            Vector3 color = ShadeTriangle(normal, viewNormal, lightDirection, shadingMode);
            RasterizeTriangle(
                pixels,
                depth,
                viewportWidth + viewportX,
                Math.Max(viewportY + viewportHeight, viewportHeight),
                screenPositions[i0],
                screenPositions[i1],
                screenPositions[i2],
                ndcDepths[i0],
                ndcDepths[i1],
                ndcDepths[i2],
                color);

            if (wireframe)
                DrawWireframe(pixels, viewportWidth + viewportX, Math.Max(viewportY + viewportHeight, viewportHeight), screenPositions[i0], screenPositions[i1], screenPositions[i2]);
        }
    }

    private static void FillBackground(byte[] pixels, int width, int height, MeshPreviewBackgroundStyle backgroundStyle)
    {
        for (int y = 0; y < height; y++)
        {
            float t = y / (float)Math.Max(1, height - 1);
            Vector3 color = backgroundStyle switch
            {
                MeshPreviewBackgroundStyle.StudioGray => Vector3.Lerp(new Vector3(0.30f, 0.31f, 0.33f), new Vector3(0.18f, 0.19f, 0.21f), t),
                MeshPreviewBackgroundStyle.FlatBlack => new Vector3(0.03f, 0.03f, 0.04f),
                MeshPreviewBackgroundStyle.Checker => CheckerBackground(y),
                _ => Vector3.Lerp(BackgroundTop, BackgroundBottom, t)
            };
            byte r = ToByte(color.X);
            byte g = ToByte(color.Y);
            byte b = ToByte(color.Z);
            for (int x = 0; x < width; x++)
            {
                int offset = ((y * width) + x) * 4;
                pixels[offset] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = 255;
            }
        }
    }

    private static Vector3 CheckerBackground(int y)
    {
        int band = (y / 24) % 2;
        return band == 0 ? new Vector3(0.14f, 0.15f, 0.17f) : new Vector3(0.11f, 0.12f, 0.14f);
    }

    private static void RasterizeTriangle(
        byte[] pixels,
        float[] depth,
        int width,
        int height,
        Vector2 p0,
        Vector2 p1,
        Vector2 p2,
        float z0,
        float z1,
        float z2,
        Vector3 color)
    {
        float area = Edge(p0, p1, p2);
        if (Math.Abs(area) < 0.0001f)
            return;

        int minX = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))));
        int maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(p0.X, MathF.Max(p1.X, p2.X))));
        int minY = Math.Max(0, (int)MathF.Floor(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))));
        int maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y))));

        byte r = ToByte(color.X);
        byte g = ToByte(color.Y);
        byte b = ToByte(color.Z);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                Vector2 p = new(x + 0.5f, y + 0.5f);
                float w0 = Edge(p1, p2, p);
                float w1 = Edge(p2, p0, p);
                float w2 = Edge(p0, p1, p);

                bool inside = area < 0
                    ? (w0 <= 0 && w1 <= 0 && w2 <= 0)
                    : (w0 >= 0 && w1 >= 0 && w2 >= 0);
                if (!inside)
                    continue;

                w0 /= area;
                w1 /= area;
                w2 /= area;

                float pixelDepth = (w0 * z0) + (w1 * z1) + (w2 * z2);
                int depthIndex = (y * width) + x;
                if (pixelDepth >= depth[depthIndex])
                    continue;

                depth[depthIndex] = pixelDepth;
                int offset = depthIndex * 4;
                pixels[offset] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = 255;
            }
        }
    }

    private static float Edge(Vector2 a, Vector2 b, Vector2 c)
    {
        return ((c.X - a.X) * (b.Y - a.Y)) - ((c.Y - a.Y) * (b.X - a.X));
    }

    private static Vector3 GetLightDirection(MeshPreviewLightingPreset preset)
    {
        return preset switch
        {
            MeshPreviewLightingPreset.Studio => StudioLightDirection,
            MeshPreviewLightingPreset.Harsh => HarshLightDirection,
            MeshPreviewLightingPreset.Soft => SoftLightDirection,
            _ => NeutralLightDirection
        };
    }

    private static Vector3 ShadeTriangle(Vector3 worldNormal, Vector3 viewNormal, Vector3 lightDirection, MeshPreviewShadingMode shadingMode)
    {
        float ndotl = Math.Clamp(Math.Max(0.0f, Vector3.Dot(worldNormal, lightDirection)), 0.0f, 1.0f);
        float facing = Math.Clamp(Math.Abs(viewNormal.Z), 0.0f, 1.0f);

        return shadingMode switch
        {
            MeshPreviewShadingMode.Studio => StudioGray * Math.Clamp(0.42f + (ndotl * 0.58f), 0.0f, 1.0f),
            MeshPreviewShadingMode.MatCap => MatCapColor * Math.Clamp(0.35f + (facing * 0.65f), 0.0f, 1.0f),
            MeshPreviewShadingMode.GameApprox => GameApproxColor * Math.Clamp(0.22f + (ndotl * 0.78f), 0.0f, 1.0f),
            MeshPreviewShadingMode.Lit => LitColor * Math.Clamp(0.26f + (ndotl * 0.74f), 0.0f, 1.0f),
            _ => ClayColor * Math.Clamp(0.28f + (ndotl * 0.72f), 0.0f, 1.0f)
        };
    }

    private static void DrawWireframe(byte[] pixels, int width, int height, Vector2 a, Vector2 b, Vector2 c)
    {
        Vector3 lineColor = new(0.05f, 0.05f, 0.05f);
        DrawLine(pixels, width, height, a, b, lineColor);
        DrawLine(pixels, width, height, b, c, lineColor);
        DrawLine(pixels, width, height, c, a, lineColor);
    }

    private static void DrawLine(byte[] pixels, int width, int height, Vector2 from, Vector2 to, Vector3 color)
    {
        int x0 = (int)MathF.Round(from.X);
        int y0 = (int)MathF.Round(from.Y);
        int x1 = (int)MathF.Round(to.X);
        int y1 = (int)MathF.Round(to.Y);
        int dx = Math.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        byte r = ToByte(color.X);
        byte g = ToByte(color.Y);
        byte b = ToByte(color.Z);

        while (true)
        {
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
            {
                int offset = ((y0 * width) + x0) * 4;
                pixels[offset] = b;
                pixels[offset + 1] = g;
                pixels[offset + 2] = r;
                pixels[offset + 3] = 255;
            }

            if (x0 == x1 && y0 == y1)
                break;

            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private static void DrawGroundPlane(byte[] pixels, float[] depth, int width, int height, Matrix4x4 viewProjection)
    {
        for (int z = -4; z <= 4; z++)
            DrawGroundLine(pixels, depth, width, height, viewProjection, new Vector3(-6f, z, 0f), new Vector3(6f, z, 0f));

        for (int x = -6; x <= 6; x++)
            DrawGroundLine(pixels, depth, width, height, viewProjection, new Vector3(x, -4f, 0f), new Vector3(x, 4f, 0f));
    }

    private static void DrawGroundLine(byte[] pixels, float[] depth, int width, int height, Matrix4x4 viewProjection, Vector3 from, Vector3 to)
    {
        if (!ProjectPoint(from, viewProjection, width, height, out Vector2 p0, out float z0) ||
            !ProjectPoint(to, viewProjection, width, height, out Vector2 p1, out float z1))
            return;

        int x0 = (int)MathF.Round(p0.X);
        int y0 = (int)MathF.Round(p0.Y);
        int x1 = (int)MathF.Round(p1.X);
        int y1 = (int)MathF.Round(p1.Y);
        int dx = Math.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int steps = Math.Max(1, Math.Max(dx, -dy));
        int step = 0;
        Vector3 color = new(0.28f, 0.29f, 0.31f);
        byte r = ToByte(color.X);
        byte g = ToByte(color.Y);
        byte b = ToByte(color.Z);

        while (true)
        {
            if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
            {
                float t = steps == 0 ? 0f : step / (float)steps;
                float pixelDepth = z0 + ((z1 - z0) * t);
                int depthIndex = (y0 * width) + x0;
                if (pixelDepth < depth[depthIndex])
                {
                    depth[depthIndex] = pixelDepth;
                    int offset = depthIndex * 4;
                    pixels[offset] = b;
                    pixels[offset + 1] = g;
                    pixels[offset + 2] = r;
                    pixels[offset + 3] = 255;
                }
            }

            if (x0 == x1 && y0 == y1)
                break;

            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }

            step++;
        }
    }

    private static bool ProjectPoint(Vector3 position, Matrix4x4 viewProjection, int width, int height, out Vector2 screen, out float depth)
    {
        Vector4 clip = Vector4.Transform(new Vector4(position, 1.0f), viewProjection);
        if (clip.W <= 0.0001f)
        {
            screen = default;
            depth = 0f;
            return false;
        }

        Vector3 ndc = new(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
        screen = new Vector2(
            (ndc.X * 0.5f + 0.5f) * (width - 1),
            (1.0f - (ndc.Y * 0.5f + 0.5f)) * (height - 1));
        depth = ndc.Z;
        return true;
    }

    private static void WritePixels(WriteableBitmap bitmap, byte[] pixels)
    {
        using Stream stream = bitmap.PixelBuffer.AsStream();
        stream.Position = 0;
        stream.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
    }

    private static byte ToByte(float value)
    {
        return (byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255);
    }
}

