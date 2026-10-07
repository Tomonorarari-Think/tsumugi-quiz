using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Linq;

using UnityEditor;

using UnityEngine;


namespace Unity.Networking.Editor
{
    [Obsolete("RoslynAnalyzerFix does not serve a purpose anymore and will be removed in a future version.")]
    public class RoslynAnalyzerFix : AssetPostprocessor
    {
        public static string OnGeneratedCSProject(string path, string content)
        {
            return content;
        }
    }
}
