﻿using Mixer.Base.Model.Broadcast;
using Mixer.Base.Model.Clips;
using MixItUp.Base.ViewModel.User;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MixItUp.Base.Actions
{
    [DataContract]
    public class MixerClipsAction : ActionBase
    {
        public const int MinimumLength = 15;
        public const int MaximumLength = 300;

        private const string VideoFileContentLocatorType = "HlsStreaming";

        private const string FFMPEGExecutablePath = "ffmpeg-4.0-win32-static\\bin\\ffmpeg.exe";

        public static string GetFFMPEGExecutablePath() { return Path.Combine(ChannelSession.Services.FileService.GetApplicationDirectory(), MixerClipsAction.FFMPEGExecutablePath); }

        private static SemaphoreSlim asyncSemaphore = new SemaphoreSlim(1);

        protected override SemaphoreSlim AsyncSemaphore { get { return MixerClipsAction.asyncSemaphore; } }

        [DataMember]
        public string ClipName { get; set; }

        [DataMember]
        public int ClipLength { get; set; }

        [DataMember]
        public bool DownloadClip { get; set; }
        [DataMember]
        public string DownloadDirectory { get; set; }

        public MixerClipsAction() : base(ActionTypeEnum.MixerClips) { }

        public MixerClipsAction(string clipName, int clipLength, bool downloadClip = false, string downloadDirectory = null)
            : this()
        {
            this.ClipName = clipName;
            this.ClipLength = clipLength;
            this.DownloadClip = downloadClip;
            this.DownloadDirectory = downloadDirectory;
        }

        protected override async Task PerformInternal(UserViewModel user, IEnumerable<string> arguments)
        {
            if (ChannelSession.Chat != null)
            {
                await ChannelSession.Chat.SendMessage("Sending clip creation request to Mixer...");

                string clipName = await this.ReplaceStringWithSpecialModifiers(this.ClipName, user, arguments);
                if (!string.IsNullOrEmpty(clipName) && MixerClipsAction.MinimumLength <= this.ClipLength && this.ClipLength <= MixerClipsAction.MaximumLength)
                {
                    bool clipCreated = false;
                    DateTimeOffset clipCreationTime = DateTimeOffset.Now;

                    BroadcastModel broadcast = await ChannelSession.Connection.GetCurrentBroadcast();
                    if (broadcast != null)
                    {
                        if (await ChannelSession.Connection.CanClipBeMade(broadcast))
                        {
                            clipCreated = await ChannelSession.Connection.CreateClip(new ClipRequestModel()
                            {
                                broadcastId = broadcast.id.ToString(),
                                highlightTitle = clipName,
                                clipDurationInSeconds = this.ClipLength
                            });
                        }
                    }

                    if (clipCreated)
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            await Task.Delay(2000);

                            IEnumerable<ClipModel> clips = await ChannelSession.Connection.GetChannelClips(ChannelSession.Channel);
                            ClipModel clip = clips.OrderByDescending(c => c.uploadDate).FirstOrDefault();
                            if (clip != null && clip.uploadDate.ToLocalTime() >= clipCreationTime && clip.title.Equals(clipName))
                            {
                                await ChannelSession.Chat.SendMessage("Clip Created: " + string.Format("https://mixer.com/{0}?clip={1}", ChannelSession.User.username, clip.shareableId));

                                if (this.DownloadClip && Directory.Exists(this.DownloadDirectory) && ChannelSession.Services.FileService.FileExists(MixerClipsAction.GetFFMPEGExecutablePath()))
                                {
                                    ClipLocatorModel clipLocator = clip.contentLocators.FirstOrDefault(cl => cl.locatorType.Equals(VideoFileContentLocatorType));
                                    if (clipLocator != null)
                                    {
                                        char[] invalidChars = Path.GetInvalidFileNameChars();
                                        string fileName = new string(clipName.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
                                        string destinationFile = Path.Combine(this.DownloadDirectory, fileName + ".mp4");

                                        Process process = new Process();
                                        process.StartInfo.FileName = MixerClipsAction.GetFFMPEGExecutablePath();
                                        process.StartInfo.Arguments = string.Format("-i {0} -c copy -bsf:a aac_adtstoasc \"{1}\"", clipLocator.uri, destinationFile);
                                        process.StartInfo.RedirectStandardOutput = true;
                                        process.StartInfo.UseShellExecute = false;
                                        process.StartInfo.CreateNoWindow = true;

                                        process.Start();
                                        while (!process.HasExited)
                                        {
                                            await Task.Delay(500);
                                        }
                                    }
                                }
                                return;
                            }
                        }
                        await ChannelSession.Chat.SendMessage("Clip was created, but could not be retrieved at this time");
                    }
                    else
                    {
                        await ChannelSession.Chat.SendMessage("Unable to create clip, please try again later");
                    }
                }
            }
        }
    }
}
