using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.UIToolkit.Figma
{
    /// <summary>
    /// The last import's review notes, stashed in SessionState for the connector window. Kept out of the
    /// pure FigmaImportReport, which the harness compiles without UnityEditor.
    /// </summary>
    static class FigmaImportReportStore
    {
        // Suffixed when the Report shape changes, so a stale entry from an older package is ignored.
        const string k_Key = "Unity.UIToolkit.Figma.LastImportReport.2";

        [Serializable]
        public class Report
        {
            public string screen;
            public int nodes;
            public bool ok;
            public bool ussErrors;
            public FigmaImportNote[] notes;
            public string when; // local HH:mm:ss, stamped at save
        }

        public static void Save(string screen, int nodes, bool ok, bool ussErrors, List<FigmaImportNote> notes)
        {
            var report = new Report
            {
                screen = screen,
                nodes = nodes,
                ok = ok,
                ussErrors = ussErrors,
                notes = notes != null ? notes.ToArray() : Array.Empty<FigmaImportNote>(),
                when = DateTime.Now.ToString("HH:mm:ss")
            };
            SessionState.SetString(k_Key, JsonUtility.ToJson(report));
        }

        /// <summary>
        /// The most recent import's report, or null if nothing imported this session.
        /// </summary>
        public static Report Load()
        {
            var json = SessionState.GetString(k_Key, string.Empty);
            if (string.IsNullOrEmpty(json))
                return null;
            return JsonUtility.FromJson<Report>(json);
        }

        public static void Clear()
        {
            SessionState.EraseString(k_Key);
        }
    }
}
