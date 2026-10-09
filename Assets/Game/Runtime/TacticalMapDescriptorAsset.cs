using System;
using System.Collections.Generic;
using UnityEngine;

namespace LocalTanks
{
    public sealed class TacticalMapDescriptorAsset : ScriptableObject
    {
        public TacticalMapDescriptor descriptor;

        public string[] Validate()
        {
            List<string> errors = new List<string>();
            if (descriptor == null)
            {
                errors.Add("Tactical map descriptor is missing.");
                return errors.ToArray();
            }

            if (descriptor.analyzerVersion != TacticalMapAnalyzer.AnalyzerVersion)
                errors.Add($"Tactical analysis version {descriptor.analyzerVersion} is stale.");
            if (string.IsNullOrWhiteSpace(descriptor.sourceHash)) errors.Add("Tactical source hash is empty.");
            if (string.IsNullOrWhiteSpace(descriptor.mapId)) errors.Add("Tactical map id is empty.");
            if (string.IsNullOrWhiteSpace(descriptor.sceneId)) errors.Add("Tactical scene id is empty.");
            if (descriptor.lanes == null || descriptor.lanes.Length == 0)
                errors.Add("Tactical map has no lanes.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (TacticalLaneDescriptor lane in descriptor.lanes ?? Array.Empty<TacticalLaneDescriptor>())
            {
                if (lane == null || string.IsNullOrWhiteSpace(lane.id) || !ids.Add(lane.id))
                    errors.Add("Tactical lane id is empty or duplicated.");
                else if (lane.pathCells == null || lane.pathCells.Length < 2)
                    errors.Add($"Tactical lane '{lane.id}' has no usable path.");
                if (lane?.suitability == null) errors.Add($"Tactical lane '{lane?.id}' has no class suitability.");
            }

            return errors.ToArray();
        }
    }
}
