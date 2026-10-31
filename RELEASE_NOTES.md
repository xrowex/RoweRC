# RoweRC 0.1.4 — smoother remote cars

Remote cars now render on engine frames instead of a separate 16 ms polling loop. One movable parent drives the whole remote assembly; static body/chassis parts no longer need individual world-transform calls every frame. Wheels still animate relative to that parent.

The Steam helper replaces its coarse Windows Forms timer with a coalesced timer that dispatches onto the same STA thread. It sends each new local snapshot, removing the extra 50 ms gate that combined with the old timer to produce roughly 10–11 pose updates per second. The game continues to export approximately 30 snapshots per second. Both PCs should update for the higher send cadence.

Interpolation uses cubic body positions, a monotonic playback clock, bounded clock-speed correction, an adaptive 120–240 ms buffer, and small-error blending after prediction. The buffer expands for slow/jittery peers; that trades some visual latency for steadier motion. Prediction remains capped at 100 ms, stale cars expire, and actual resets snap instead of sliding.

Suspension compression no longer changes the appearance identity or repeatedly replaces the car. Actual body/paint/rim/tire choices still update. Appearance scans reuse component/material lookups, and UI/file polling is throttled independently of rendering.

Validation: warning-free Release build; 70 backend checks, 17 smoothing checks, six appearance regression checks and five Lua syntax checks. A native local fixture measured 202.9 remote-render updates/s with a 20 Hz input, a 146 ms buffer and zero predicted frames in that sample; local export measured 30.5 Hz. These are observed samples, not guaranteed frame rates. All 80 appearance parts remained intact and peer cleanup completed without a Lua error. Fresh two-PC driving acceptance is still needed to judge the remaining jitter and new send cadence.

Update from F8 → Check for mod update, wait for the download, then close and reopen RC Tandems. Existing signed updates, host maps and missing-body fixes remain included.
