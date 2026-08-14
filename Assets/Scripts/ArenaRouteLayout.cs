using System;
using UnityEngine;

/// <summary>
/// Scene-authored references for the continuous P -> R -> O route.
/// </summary>
public sealed class ArenaRouteLayout : MonoBehaviour
{
    [SerializeField] private int _setupVersion;
    [SerializeField] private ArenaZone _arenaP;
    [SerializeField] private ArenaZone _arenaR;
    [SerializeField] private ArenaZone _arenaO;
    [SerializeField] private ArenaGate _gatePToR;
    [SerializeField] private ArenaGate _gateRToO;
    [SerializeField] private ArenaCapturePoint[] _capturePointsR = Array.Empty<ArenaCapturePoint>();
    [SerializeField] private ArenaBossBoundary _bossBoundaryO;

    public ArenaZone ArenaP => _arenaP;
    public ArenaZone ArenaR => _arenaR;
    public ArenaZone ArenaO => _arenaO;
    public ArenaGate GatePToR => _gatePToR;
    public ArenaGate GateRToO => _gateRToO;
    public ArenaCapturePoint[] CapturePointsR => _capturePointsR;
    public ArenaBossBoundary BossBoundaryO => _bossBoundaryO;
    public int SetupVersion => _setupVersion;

    public void Configure(
        ArenaZone arenaP,
        ArenaZone arenaR,
        ArenaZone arenaO,
        ArenaGate gatePToR,
        ArenaGate gateRToO,
        ArenaCapturePoint[] capturePointsR,
        ArenaBossBoundary bossBoundaryO)
    {
        _arenaP = arenaP;
        _arenaR = arenaR;
        _arenaO = arenaO;
        _gatePToR = gatePToR;
        _gateRToO = gateRToO;
        _capturePointsR = capturePointsR ?? Array.Empty<ArenaCapturePoint>();
        _bossBoundaryO = bossBoundaryO;
        _setupVersion = ArenaRouteSceneVersion.Current;
    }

    public void MarkSetupVersion(int version)
    {
        _setupVersion = Mathf.Max(0, version);
    }

    public bool ValidateConfiguration(out string error)
    {
        if (_arenaP == null || _arenaR == null || _arenaO == null)
        {
            error = "All three arena zones P, R and O must be assigned.";
            return false;
        }

        if (_gatePToR == null || _gateRToO == null)
        {
            error = "Both route gates P->R and R->O must be assigned.";
            return false;
        }

        if (_capturePointsR == null || _capturePointsR.Length != 3)
        {
            error = "Arena R must have exactly three capture points.";
            return false;
        }

        foreach (ArenaCapturePoint capturePoint in _capturePointsR)
        {
            if (capturePoint == null)
            {
                error = "Arena R contains an unassigned capture point.";
                return false;
            }
        }

        if (_bossBoundaryO == null)
        {
            error = "Arena O boss boundary must be assigned.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}

public static class ArenaRouteSceneVersion
{
    public const int Current = 5;
}
