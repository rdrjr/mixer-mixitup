﻿using MixItUp.Base.Util;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Runtime.Serialization;

namespace MixItUp.Base.Overlay
{
    [DataContract]
    public class OverlayImage
    {
        [DataMember]
        public string imagePath;
        [DataMember]
        public int duration;
        [DataMember]
        public int horizontal;
        [DataMember]
        public int vertical;
        [DataMember]
        public int width;
        [DataMember]
        public int height;
        [DataMember]
        public string imageData;
    }

    [DataContract]
    public class OverlayText
    {
        [DataMember]
        public string text;
        [DataMember]
        public string color;
        [DataMember]
        public int duration;
        [DataMember]
        public int horizontal;
        [DataMember]
        public int vertical;
        [DataMember]
        public int fontSize;
    }

    public class OverlayWebServer : RequestSenderWebServerBase
    {
        private Process nodeJSProcess;

        public OverlayWebServer(string address)
            : base(address)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            startInfo.UseShellExecute = false;
            startInfo.FileName = "Overlay\\LaunchNode.bat";

            this.nodeJSProcess = Process.Start(startInfo);
        }

        public override void Close()
        {
            base.Close();
            this.nodeJSProcess.Close();
        }

        public void SetImage(OverlayImage image) { this.SendData("image", JObject.FromObject(image)); }

        public void SetText(OverlayText text) { this.SendData("text", JObject.FromObject(text)); }
    }
}