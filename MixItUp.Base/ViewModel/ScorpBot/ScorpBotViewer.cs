﻿using System;
using System.Data.Common;
using System.Runtime.Serialization;

namespace MixItUp.Base.ViewModel.ScorpBot
{
    [DataContract]
    public class ScorpBotViewer
    {
        [DataMember]
        public uint ID { get; set; }

        [DataMember]
        public string UserName { get; set; }

        [DataMember]
        public int Type { get; set; }
        [DataMember]
        public string Rank { get; set; }

        [DataMember]
        public long Points1 { get; set; }
        [DataMember]
        public long Points2 { get; set; }

        [DataMember]
        public int Hours { get; set; }

        [DataMember]
        public string Sub { get; set; }

        public ScorpBotViewer() { }

        public ScorpBotViewer(DbDataReader reader)
        {
            this.ID = uint.Parse((string)reader["BeamID"]);
            this.UserName = (string)reader["BeamName"];
            this.Type = (int)reader["Type"];
            this.Rank = (string)reader["Rank"];
            this.Points1 = (long)reader["Points"];
            this.Points2 = (long)reader["Points2"];
            this.Hours = int.Parse((string)reader["Hours"]);
            this.Sub = (string)reader["Sub"];
        }
    }
}