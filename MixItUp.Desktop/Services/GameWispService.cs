﻿using Mixer.Base;
using Mixer.Base.Model.OAuth;
using MixItUp.Base;
using MixItUp.Base.Services;
using MixItUp.Base.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Quobject.SocketIoClientDotNet.Client;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MixItUp.Desktop.Services
{
    public class GameWispWebSocketService
    {
        [DataContract]
        private class GameWispWebSocketEvent
        {
            [JsonProperty("event")]
            public string Event { get; set; }

            [JsonProperty("channel_id")]
            public string ChannelID { get; set; }

            [JsonProperty("channel")]
            public JObject Channel { get; set; }

            [JsonProperty("data")]
            public JObject Data { get; set; }
        }

        private GameWispService service;
        private OAuthTokenModel oauthToken;

        private Socket socket;

        private string sessionID;
        private string channelID;

        public GameWispWebSocketService(GameWispService service, OAuthTokenModel oauthToken)
        {
            this.service = service;
            this.oauthToken = oauthToken;
        }

        public bool Connected { get { return !string.IsNullOrEmpty(this.sessionID); } }
        public bool Authenticated { get { return !string.IsNullOrEmpty(this.channelID); } }

        public async Task Connect()
        {
            this.socket = IO.Socket("https://singularity.gamewisp.com");

            this.SocketReceiveWrapper("unauthorized", (errorData) =>
            {
                Logger.Log(errorData.ToString());
            });

            this.SocketReceiveWrapper("disconnect", (errorData) =>
            {
                Logger.Log(errorData.ToString());
                this.service.WebSocketDisconnectedOccurred();
            });

            this.SocketReceiveWrapper("connect", (connectData) =>
            {
                JObject jobj = new JObject();
                jobj["key"] = GameWispService.ClientID;
                jobj["access_token"] = this.oauthToken.accessToken;
                this.SocketSendWrapper("authentication", jobj);
            });

            this.SocketReceiveWrapper("authenticated", (authData) =>
            {
                if (authData != null)
                {
                    JObject jobj = (JObject)authData;
                    if (jobj["session"] != null)
                    {
                        this.sessionID = jobj["session"].ToString();
                    }
                }
            });

            this.SocketReceiveWrapper("app-channel-connected", (appChannelData) =>
            {
                if (this.Connected && appChannelData != null)
                {
                    JObject jobj = JObject.Parse(appChannelData.ToString());
                    if (jobj != null && jobj["data"] != null && jobj["data"]["channel_id"] != null)
                    {
                        this.channelID = jobj["data"]["channel_id"].ToString();
                    }
                }
            });

            this.SocketEventReceiverWrapper("subscriber-new", (eventData) =>
            {
                this.service.SubscribeOccurred(new GameWispSubscribeEvent(eventData.Data));
            });

            this.SocketEventReceiverWrapper("subscriber-renewed", (eventData) =>
            {
                this.service.ResubscribeOccurred(new GameWispResubscribeEvent(eventData.Data));
            });

            this.SocketEventReceiverWrapper("subscriber-benefits-change", (eventData) =>
            {
                this.service.SubscriberBenefitsChangeOccurred(new GameWispBenefitsChangeEvent(eventData.Data));
            });

            this.SocketEventReceiverWrapper("subscriber-status-change", (eventData) =>
            {
                this.service.SubscriberStatusChangeOccurred(new GameWispSubscribeEvent(eventData.Data));
            });

            this.SocketEventReceiverWrapper("subscriber-anniversary", (eventData) =>
            {
                this.service.SubscriberAnniversaryOccurred(new GameWispAnniversaryEvent(eventData.Data));
            });

            for (int i = 0; i < 5 && (!this.Connected && !this.Authenticated); i++)
            {
                await Task.Delay(1000);
            }

            if (this.Connected && this.Authenticated)
            {
                this.service.WebSocketConnectedOccurred();
            }
        }

        public Task Disconnect()
        {
            if (this.socket != null)
            {
                JObject channelDisconnectJObject = new JObject();
                channelDisconnectJObject["access_token"] = this.oauthToken.accessToken;
                this.SocketSendWrapper("channel-disconnect", channelDisconnectJObject);
                this.socket.Close();
            }
            this.socket = null;
            this.sessionID = null;
            this.channelID = null;

            return Task.FromResult(0);
        }

        private void SocketReceiveWrapper(string eventString, Action<object> processEvent)
        {
            this.socket.On(eventString, (eventData) =>
            {
                try
                {
                    processEvent(eventData);
                }
                catch (Exception ex) { Logger.Log(ex); }
            });
        }

        private void SocketEventReceiverWrapper(string eventString, Action<GameWispWebSocketEvent> processEvent)
        {
            this.SocketReceiveWrapper(eventString, (eventData) =>
            {
                if (this.Connected && this.Authenticated)
                {
                    JObject jobj = JObject.Parse(eventData.ToString());
                    if (jobj != null)
                    {
                        processEvent(jobj.ToObject<GameWispWebSocketEvent>());
                    }
                }
            });
        }

        private void SocketSendWrapper(string eventString, object data)
        {
            try
            {
                this.socket.Emit(eventString, data);
            }
            catch (Exception ex) { Logger.Log(ex); }
        }
    }

    public class GameWispService : OAuthServiceBase, IGameWispService
    {
        public const string ClientID = "0b23546e4f147c63509c29928f2bf87e73ce62f";

        private const string BaseAddress = "https://api.gamewisp.com/pub/v1/";

        private const string StateKey = "SGVsbG9Xb3JsZCE=";
        private const string AuthorizationUrl = "https://api.gamewisp.com/pub/v1/oauth/authorize?response_type=code&client_id={0}&redirect_uri=http://localhost:8919/&scope=read_only,subscriber_read_full,user_read&state=optional%20base64%20encoded%20state%20string";

        private const int PagedItemLimit = 50;

        public GameWispChannelInformation ChannelInfo { get; private set; }

        public bool WebSocketConnectedAndAuthenticated { get { return (this.socket != null && this.socket.Connected && this.socket.Authenticated); } }

        public event EventHandler OnWebSocketConnectedOccurred;
        public event EventHandler OnWebSocketDisconnectedOccurred;

        public event EventHandler<GameWispSubscribeEvent> OnSubscribeOccurred;
        public event EventHandler<GameWispResubscribeEvent> OnResubscribeOccurred;
        public event EventHandler<GameWispBenefitsChangeEvent> OnSubscriberBenefitsChangeOccurred;
        public event EventHandler<GameWispSubscribeEvent> OnSubscriberStatusChangeOccurred;
        public event EventHandler<GameWispAnniversaryEvent> OnSubscriberAnniversaryOccurred;

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        private GameWispWebSocketService socket;

        public GameWispService() : base(GameWispService.BaseAddress) { }

        public GameWispService(OAuthTokenModel token) : base(GameWispService.BaseAddress, token) { }

        public async Task<bool> Connect()
        {
            if (this.token != null)
            {
                try
                {
                    await this.RefreshOAuthToken();

                    await this.InitializeInternal();

                    return true;
                }
                catch (Exception ex) { Logger.Log(ex); }
            }

            string authorizationCode = await this.ConnectViaOAuthRedirect(string.Format(GameWispService.AuthorizationUrl, GameWispService.ClientID, GameWispService.StateKey));
            if (!string.IsNullOrEmpty(authorizationCode))
            {
                var body = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("grant_type", "authorization_code"),
                    new KeyValuePair<string, string>("client_id", GameWispService.ClientID),
                    new KeyValuePair<string, string>("client_secret", ChannelSession.SecretManager.GetSecret("GameWispSecret")),
                    new KeyValuePair<string, string>("redirect_uri", MixerConnection.DEFAULT_OAUTH_LOCALHOST_URL),
                    new KeyValuePair<string, string>("code", authorizationCode),
                };
                this.token = await this.GetWWWFormUrlEncodedOAuthToken(BaseAddress + "oauth/token", ClientID, ChannelSession.SecretManager.GetSecret("GameWispSecret"), body);
                if (this.token != null)
                {
                    token.authorizationCode = authorizationCode;

                    await this.InitializeInternal();

                    return true;
                }
            }

            return false;
        }

        public Task Disconnect()
        {
            this.token = null;
            this.cancellationTokenSource.Cancel();
            return Task.FromResult(0);
        }

        public async Task<GameWispChannelInformation> GetChannelInformation()
        {
            return await this.GetDataWithAccessTokenAsync<GameWispChannelInformation>("channel/information?include=tiers,sponsor_counts");
        }

        public async Task<IEnumerable<GameWispSubscriber>> GetSubscribers()
        {
            return await this.GetPagedWithAccessTokenAsync<GameWispSubscriber>("channel/subscribers?include=user");
        }

        public async Task<GameWispSubscriber> GetSubscriber(string username)
        {
            IEnumerable<GameWispSubscriber> data = await this.GetPagedWithAccessTokenAsync<GameWispSubscriber>(string.Format("channel/subscriber-for-channel?type=gamewisp&include=user,benefits,anniversaries&user_name={0}", username));
            return data.FirstOrDefault();
        }

        public void WebSocketConnectedOccurred()
        {
            if (this.OnWebSocketConnectedOccurred != null)
            {
                this.OnWebSocketConnectedOccurred(this, new EventArgs());
            }
        }

        public async void WebSocketDisconnectedOccurred()
        {
            if (this.OnWebSocketDisconnectedOccurred != null)
            {
                this.OnWebSocketDisconnectedOccurred(this, new EventArgs());
            }
            await this.ReconnectWebSocket();
        }

        public void SubscribeOccurred(GameWispSubscribeEvent eventData)
        {
            if (this.OnSubscribeOccurred != null)
            {
                this.OnSubscribeOccurred(this, eventData);
            }
        }

        public void ResubscribeOccurred(GameWispResubscribeEvent eventData)
        {
            if (this.OnResubscribeOccurred != null)
            {
                this.OnResubscribeOccurred(this, eventData);
            }
        }

        public void SubscriberBenefitsChangeOccurred(GameWispBenefitsChangeEvent eventData)
        {
            if (this.OnSubscriberBenefitsChangeOccurred != null)
            {
                this.OnSubscriberBenefitsChangeOccurred(this, eventData);
            }
        }

        public void SubscriberStatusChangeOccurred(GameWispSubscribeEvent eventData)
        {
            if (this.OnSubscriberStatusChangeOccurred != null)
            {
                this.OnSubscriberStatusChangeOccurred(this, eventData);
            }
        }

        public void SubscriberAnniversaryOccurred(GameWispAnniversaryEvent eventData)
        {
            if (this.OnSubscriberAnniversaryOccurred != null)
            {
                this.OnSubscriberAnniversaryOccurred(this, eventData);
            }
        }

        protected override async Task RefreshOAuthToken()
        {
            if (this.token != null)
            {
                var body = new List<KeyValuePair<string, string>>
                {
                    new KeyValuePair<string, string>("grant_type", "refresh_token"),
                    new KeyValuePair<string, string>("client_id", GameWispService.ClientID),
                    new KeyValuePair<string, string>("client_secret", ChannelSession.SecretManager.GetSecret("GameWispSecret")),
                    new KeyValuePair<string, string>("redirect_uri", MixerConnection.DEFAULT_OAUTH_LOCALHOST_URL),
                    new KeyValuePair<string, string>("refresh_token", this.token.refreshToken),
                };
                this.token = await this.GetWWWFormUrlEncodedOAuthToken(BaseAddress + "oauth/token", ClientID, ChannelSession.SecretManager.GetSecret("GameWispSecret"), body);
            }
        }

        private async Task InitializeInternal()
        {
            this.ChannelInfo = await this.GetChannelInformation();
            this.socket = new GameWispWebSocketService(this, await this.GetOAuthToken());
            await this.socket.Connect();
        }

        private async Task ReconnectWebSocket()
        {
            do
            {
                await this.socket.Disconnect();

                await Task.Delay(1000);

                await this.socket.Connect();
            } while (!this.WebSocketConnectedAndAuthenticated);
        }

        private async Task<T> GetDataWithAccessTokenAsync<T>(string requestUri)
        {
            GameWispResponse<T> response = await this.GetWithAccessTokenAsync<GameWispResponse<T>>(requestUri);
            return (response.HasData) ? response.GetResponseData() : default(T);
        }

        private async Task<IEnumerable<T>> GetPagedWithAccessTokenAsync<T>(string requestUri)
        {
            List<T> results = new List<T>();
            string nextPage = null;

            do
            {
                string currentRequestUri = requestUri;
                if (!string.IsNullOrEmpty(nextPage))
                {
                    currentRequestUri += "&cursor=" + nextPage;
                }
                currentRequestUri += "&limit=" + PagedItemLimit;

                GameWispArrayResponse<T> response = await this.GetWithAccessTokenAsync<GameWispArrayResponse<T>>(currentRequestUri);
                if (response.HasData)
                {
                    results.AddRange(response.GetResponseData());
                }

                GameWispCursor cursor = response.GetCursor();
                if (cursor != null && cursor.Count == PagedItemLimit)
                {
                    nextPage = cursor.Next;
                }
                else
                {
                    nextPage = null;
                }
            } while (!string.IsNullOrEmpty(nextPage));

            return results;
        }

        private async Task<T> GetWithAccessTokenAsync<T>(string requestUri)
        {
            return await this.GetAsync<T>(requestUri + (requestUri.Contains("?") ? "&" : "?") + string.Format("access_token={0}", this.token.accessToken));
        }
    }
}
