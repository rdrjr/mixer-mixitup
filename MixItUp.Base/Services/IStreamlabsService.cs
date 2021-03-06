﻿using Mixer.Base.Model.OAuth;
using Mixer.Base.Util;
using MixItUp.Base.Model.User;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace MixItUp.Base.Services
{
    public class StreamlabsDonation
    {
        [JsonProperty("donation_id")]
        public int ID { get; set; }

        [JsonProperty("name")]
        public string UserName { get; set; }
        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("amount")]
        public string AmountString { get; set; }

        [JsonProperty("created_at")]
        public long CreatedAt { get; set; }

        [JsonIgnore]
        public double Amount
        {
            get
            {
                double amount = 0;
                double.TryParse(this.AmountString, out amount);
                return amount;
            }
        }

        public StreamlabsDonation()
        {
            this.CreatedAt = DateTimeHelper.DateTimeOffsetToUnixTimestamp(DateTimeOffset.Now);
        }

        public UserDonationModel ToGenericDonation()
        {
            return new UserDonationModel()
            {
                Source = UserDonationSourceEnum.Streamlabs,

                ID = this.ID.ToString(),
                UserName = this.UserName,
                Message = this.Message,

                Amount = Math.Round(this.Amount, 2),

                DateTime = DateTimeHelper.UnixTimestampToDateTimeOffset(this.CreatedAt),
            };
        }
    }

    public interface IStreamlabsService
    {
        Task<bool> Connect();

        Task Disconnect();

        OAuthTokenModel GetOAuthTokenCopy();

        Task SpinWheel();
        
        Task EmptyJar();

        Task RollCredits();
    }
}
