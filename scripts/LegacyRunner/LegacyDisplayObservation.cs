using System;
using System.Collections.Generic;
using System.Linq;

namespace gEmuera.LegacyRunner
{
    public sealed class LegacyDisplayRect
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
    }

    public sealed class LegacyDisplayPoint
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    public sealed class LegacyDisplayCoverageEvidence
    {
        public string Status { get; set; } = "Uncovered";
        public int Count { get; set; }
        public string Note { get; set; } = "";

        public void SetObserved(int count, string observedNote, string uncoveredNote)
        {
            Count = Math.Max(0, count);
            Status = Count > 0 ? "Captured" : "Uncovered";
            Note = Count > 0 ? observedNote ?? "" : uncoveredNote ?? "";
        }
    }

    public sealed class LegacyDisplayFeatureCoverage
    {
        public LegacyDisplayCoverageEvidence Div { get; set; } = new LegacyDisplayCoverageEvidence();
        public LegacyDisplayCoverageEvidence NestedDiv { get; set; } = new LegacyDisplayCoverageEvidence();
        public LegacyDisplayCoverageEvidence Src { get; set; } = new LegacyDisplayCoverageEvidence();
        public LegacyDisplayCoverageEvidence Srcb { get; set; } = new LegacyDisplayCoverageEvidence();
        public LegacyDisplayCoverageEvidence DynamicMap { get; set; } = new LegacyDisplayCoverageEvidence();
        public LegacyDisplayCoverageEvidence DataOnly { get; set; } = new LegacyDisplayCoverageEvidence();
        public LegacyDisplayCoverageEvidence ScrollIntent { get; set; } = new LegacyDisplayCoverageEvidence();
    }

    public sealed class LegacyDisplayBackendEvidence
    {
        public int RetainedLines { get; set; }
        public int LayoutEntries { get; set; }
        public int LineContainerChildren { get; set; }
        public int ControlRowNodes { get; set; }
        public int CanvasRows { get; set; }
        public int PositionedRows { get; set; }
        public int EscapedRows { get; set; }
        public int ImageOverlayRows { get; set; }
        public int ImageOverlayNodes { get; set; }
        public int DivOverlayRows { get; set; }
        public int DivOverlayNodes { get; set; }
        public int AnimatedOverlays { get; set; }
        public float TotalHeight { get; set; }
        public float WidestLine { get; set; }
    }

    public sealed class LegacyDisplayViewportEvidence
    {
        public LegacyDisplayRect Viewport { get; set; } = new LegacyDisplayRect();
        public LegacyDisplayRect SafeArea { get; set; } = new LegacyDisplayRect();
        public LegacyDisplayRect ContentViewport { get; set; } = new LegacyDisplayRect();
        public int ScrollX { get; set; }
        public int ScrollY { get; set; }
        public float ContentScale { get; set; } = 1.0f;
    }

    public sealed class LegacyDisplayHitProbe
    {
        public LegacyDisplayPoint Point { get; set; } = new LegacyDisplayPoint();
        public bool Hit { get; set; }
        public string Value { get; set; } = "";
        public long Generation { get; set; }
    }

    public sealed class LegacyDisplayHitEvidence
    {
        public string Backend { get; set; } = "";
        public string Source { get; set; } = "";
        public string CoordinateSpace { get; set; } = "viewport-global";
        public LegacyDisplayRect Rect { get; set; } = new LegacyDisplayRect();
        public string Value { get; set; } = "";
        public long Generation { get; set; }
        public LegacyDisplayHitProbe Probe { get; set; } = new LegacyDisplayHitProbe();
        public bool Matched { get; set; }
    }

    public sealed class LegacyScreenshotEvidence
    {
        public string Status { get; set; } = "Uncovered";
        public string FileName { get; set; } = "";
        public string Sha256 { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; } = "png";
        public string FrameSync { get; set; } = "RenderingServer.frame_post_draw";
        public string Failure { get; set; } = "capture_not_requested";
    }

    public sealed class LegacyDisplayObservation
    {
        public const string CurrentSchemaVersion = "1.0.0";

        public string SchemaVersion { get; set; } = CurrentSchemaVersion;
        public string RequestedBackend { get; set; } = "";
        public string EffectiveBackend { get; set; } = "";
        public LegacyDisplayBackendEvidence BackendEvidence { get; set; } = new LegacyDisplayBackendEvidence();
        public LegacyDisplayViewportEvidence Viewport { get; set; } = new LegacyDisplayViewportEvidence();
        public LegacyScreenshotEvidence Screenshot { get; set; } = new LegacyScreenshotEvidence();
        public LegacyDisplayFeatureCoverage FeatureCoverage { get; set; } = new LegacyDisplayFeatureCoverage();
        public List<LegacyDisplayHitEvidence> Hits { get; set; } = new List<LegacyDisplayHitEvidence>();
        public List<string> Uncovered { get; set; } = new List<string>();

        public static LegacyDisplayObservation CreateUncovered(string requestedBackend, string reason)
        {
            var observation = new LegacyDisplayObservation
            {
                RequestedBackend = requestedBackend ?? "",
                EffectiveBackend = "unavailable"
            };
            observation.Uncovered.Add(reason ?? "legacy_display_surface_unavailable");
            observation.RefreshCoverageUncovered();
            return observation;
        }

        public void ApplyTraceCoverage(LegacyTraceSnapshot trace)
        {
            int dataOnlyLines = 0;
            var scrollIntents = new HashSet<string>(StringComparer.Ordinal);
            if (trace?.Events != null)
            {
                foreach (var item in trace.Events)
                {
                    if (item?.Payload is not LegacyTraceDisplayPayload payload)
                        continue;
                    dataOnlyLines += Math.Max(0, payload.DataOnlyLineCount);
                    if (!string.IsNullOrWhiteSpace(payload.ScrollIntent))
                        scrollIntents.Add(payload.ScrollIntent);
                }
            }
            FeatureCoverage.DataOnly.SetObserved(dataOnlyLines,
                "typed display trace data-only line count", "no data-only display transaction reached by this replay");
            FeatureCoverage.ScrollIntent.SetObserved(scrollIntents.Count,
                "distinct typed display trace scroll intents", "no display scroll intent reached by this replay");
            RefreshCoverageUncovered();
        }

        public void RefreshCoverageUncovered()
        {
            Uncovered.RemoveAll(value => value != null && value.StartsWith("feature:", StringComparison.Ordinal));
            AddCoverageUncovered("div", FeatureCoverage.Div);
            AddCoverageUncovered("nestedDiv", FeatureCoverage.NestedDiv);
            AddCoverageUncovered("src", FeatureCoverage.Src);
            AddCoverageUncovered("srcb", FeatureCoverage.Srcb);
            AddCoverageUncovered("dynamicMap", FeatureCoverage.DynamicMap);
            AddCoverageUncovered("dataOnly", FeatureCoverage.DataOnly);
            AddCoverageUncovered("scrollIntent", FeatureCoverage.ScrollIntent);
            if (Hits == null || Hits.Count == 0)
                AddUniqueUncovered("hit-test:no visible console button was available for a real backend probe");
            else if (Hits.Any(hit => hit == null || !hit.Matched))
                AddUniqueUncovered("hit-test:one or more backend probes did not match their source metadata");
            else
                Uncovered.RemoveAll(value => string.Equals(value,
                    "hit-test:no visible console button was available for a real backend probe", StringComparison.Ordinal)
                    || string.Equals(value, "hit-test:one or more backend probes did not match their source metadata", StringComparison.Ordinal));
            Uncovered.RemoveAll(value => value != null && value.StartsWith("screenshot:", StringComparison.Ordinal));
            if (!string.Equals(Screenshot?.Status, "Captured", StringComparison.Ordinal))
                AddUniqueUncovered("screenshot:" + (Screenshot?.Failure ?? "not_captured"));
        }

        void AddCoverageUncovered(string name, LegacyDisplayCoverageEvidence evidence)
        {
            if (!string.Equals(evidence?.Status, "Captured", StringComparison.Ordinal))
                AddUniqueUncovered("feature:" + name + ":" + (evidence?.Note ?? "not_observed"));
        }

        void AddUniqueUncovered(string value)
        {
            if (!Uncovered.Contains(value, StringComparer.Ordinal))
                Uncovered.Add(value);
        }
    }
}
