using System;
using System.Windows;
using System.Windows.Media;

namespace LyricalWidget.Lyrics;

/// <summary>
/// 2D camera controller managing pan, zoom, and rotation via MatrixTransform.
/// </summary>
public sealed class KineticCameraController
{
    public double CamX { get; set; } = 0.0;
    public double CamY { get; set; } = 0.0;
    public double CamZoom { get; set; } = 1.0;
    public double CamAngle { get; set; } = 0.0;

    public double TargetCamX { get; set; } = 0.0;
    public double TargetCamY { get; set; } = 0.0;
    public double TargetCamZoom { get; set; } = 1.0;
    public double TargetCamAngle { get; set; } = 0.0;

    public double BaseZoom { get; set; } = 1.0;
    public double BeatPulseZoom { get; private set; } = 0.0;
    public double ZoomPunch { get; private set; } = 0.0;
    public double CameraSpeed { get; set; } = 10.5;

    public void TriggerZoomPunch(double multiplier = 1.35)
    {
        ZoomPunch = multiplier - 1.0;
    }

    public void Reset(Point anchor)
    {
        SnapTo(anchor.X, anchor.Y, 1.0, 0.0);
    }

    public void SnapTo(double x, double y, double zoom, double angle)
    {
        if (!double.IsFinite(x)) x = 0.0;
        if (!double.IsFinite(y)) y = 0.0;
        if (!double.IsFinite(zoom) || zoom < 0.2 || zoom > 3.0) zoom = 1.0;
        if (!double.IsFinite(angle)) angle = 0.0;

        angle = Math.Clamp(angle, -90.0, 90.0);

        CamX = TargetCamX = x;
        CamY = TargetCamY = y;
        CamZoom = TargetCamZoom = zoom;
        CamAngle = TargetCamAngle = angle;
        BaseZoom = zoom;
        BeatPulseZoom = 0.0;
        ZoomPunch = 0.0;
    }

    public void FocusPhrase(KineticPhraseVisual phrase, double lineDurationMs)
    {
        BaseZoom = phrase.BaseZoom;
        TargetCamX = phrase.WorldAnchor.X;
        TargetCamY = phrase.WorldAnchor.Y;
        TargetCamZoom = phrase.BaseZoom;
        TargetCamAngle = Math.Clamp(phrase.TargetCameraAngle, -90.0, 90.0);

        if (phrase.IsPosterMode)
        {
            CameraSpeed = 20.0;
        }
        else
        {
            if (lineDurationMs < 1100.0)
            {
                CameraSpeed = 16.0;
            }
            else if (lineDurationMs < 2800.0)
            {
                CameraSpeed = 10.5;
            }
            else
            {
                CameraSpeed = 6.5;
            }
        }
    }

    public void NudgeTowardWord(Point wordWorldCenter, double factor = 0.05)
    {
        if (double.IsFinite(wordWorldCenter.X) && double.IsFinite(wordWorldCenter.Y))
        {
            TargetCamX += (wordWorldCenter.X - TargetCamX) * factor;
            TargetCamY += (wordWorldCenter.Y - TargetCamY) * factor;
        }
    }

    public void SetZoomCreep(double progressRatio)
    {
        // Continuous subtle creep (1.00 -> 1.04) over line duration so camera is never static
        double creep = 1.00 + (Math.Clamp(progressRatio, 0.0, 1.0) * 0.04);
        TargetCamZoom = BaseZoom * creep;
    }

    public void Update(double dt, double timeElapsedSec, double bassEnergy, double beatImpulse, string vibeSetting)
    {
        if (dt <= 0.0) return;
        dt = Math.Clamp(dt, 0.001, 0.033);

        double speed = CameraSpeed;
        if (vibeSetting == "Calm") speed *= 0.75;
        else if (vibeSetting == "Rap") speed *= 1.25;

        // Subtle beat pulse micro-push (>0.50 impulse)
        if (beatImpulse > 0.50)
        {
            double push = (beatImpulse - 0.50) * 0.025;
            BeatPulseZoom = Math.Clamp(push, 0.0, 0.015);
        }
        BeatPulseZoom *= Math.Exp(-dt / 0.12);
        if (BeatPulseZoom < 0.001) BeatPulseZoom = 0.0;

        ZoomPunch *= Math.Exp(-dt / 0.08);
        if (ZoomPunch < 0.001) ZoomPunch = 0.0;

        // Frame-rate-independent exponential smoothing: (1 - exp(-speed * dt))
        double posFactor = 1.0 - Math.Exp(-speed * dt);
        CamX += (TargetCamX - CamX) * posFactor;
        CamY += (TargetCamY - CamY) * posFactor;

        double zoomFactor = 1.0 - Math.Exp(-(speed * 1.05) * dt);
        CamZoom += (TargetCamZoom - CamZoom) * zoomFactor;

        double angleFactor = 1.0 - Math.Exp(-(speed * 0.90) * dt);
        CamAngle += (TargetCamAngle - CamAngle) * angleFactor;

        // Bounded Camera Math Guard
        if (!double.IsFinite(CamX) || !double.IsFinite(TargetCamX)) CamX = TargetCamX = 0.0;
        if (!double.IsFinite(CamY) || !double.IsFinite(TargetCamY)) CamY = TargetCamY = 0.0;
        if (!double.IsFinite(CamZoom) || CamZoom < 0.3 || CamZoom > 3.0) CamZoom = TargetCamZoom = Math.Clamp(CamZoom, 0.55, 1.45);
        if (!double.IsFinite(CamAngle)) CamAngle = TargetCamAngle = 0.0;

        CamAngle = Math.Clamp(CamAngle, -90.0, 90.0);
        TargetCamAngle = Math.Clamp(TargetCamAngle, -90.0, 90.0);
    }

    public void ApplyMatrix(double vpW, double vpH, MatrixTransform targetTransform, double timeElapsedSec)
    {
        if (!double.IsFinite(vpW) || vpW <= 100) vpW = 1920;
        if (!double.IsFinite(vpH) || vpH <= 100) vpH = 1080;

        // Subtle ambient drift (so screen is NEVER frozen dead still)
        double ambientZoom = 1.0 + (Math.Sin(timeElapsedSec * 0.35) * 0.008);
        double ambientPanX = Math.Cos(timeElapsedSec * 0.25) * 3.5;
        double ambientPanY = Math.Sin(timeElapsedSec * 0.30) * 2.5;

        double finalCamX = CamX + ambientPanX;
        double finalCamY = CamY + ambientPanY;
        double finalZoom = CamZoom * (1.0 + BeatPulseZoom + ZoomPunch) * ambientZoom;

        // Safe area framing: centered cleanly above/right of bottom-left album badge
        double safeCenterX = vpW * 0.52;
        double safeCenterY = vpH * 0.46;

        var m = Matrix.Identity;
        m.Translate(-finalCamX, -finalCamY);
        m.Rotate(-CamAngle);
        m.Scale(finalZoom, finalZoom);
        m.Translate(safeCenterX, safeCenterY);

        targetTransform.Matrix = m;
    }
}
