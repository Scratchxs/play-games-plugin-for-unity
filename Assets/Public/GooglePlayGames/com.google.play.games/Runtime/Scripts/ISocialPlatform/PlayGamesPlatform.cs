// <copyright file="PlayGamesPlatform.cs" company="Google Inc.">
// Copyright (C) 2014 Google Inc. All Rights Reserved.
//
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//
//  http://www.apache.org/licenses/LICENSE-2.0
//
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//    limitations under the License.
// </copyright>

#if UNITY_ANDROID

namespace GooglePlayGames
{
    using System;
    using System.Collections.Generic;
    using GooglePlayGames.BasicApi;
    using GooglePlayGames.BasicApi.Events;
    using GooglePlayGames.BasicApi.Nearby;
    using GooglePlayGames.BasicApi.SavedGame;
    using GooglePlayGames.OurUtils;
    using UnityEngine;
    using UnityEngine.SocialPlatforms;

    /// <summary>
    /// Provides access to the Google Play Games platform. This is an implementation of
    /// UnityEngine.SocialPlatforms.ISocialPlatform. Activate this platform by calling
    /// the <see cref="Activate" /> method, then authenticate by calling
    /// the <see cref="Authenticate" /> method. After authentication
    /// completes, you may call the other methods of this class. This is not a complete
    /// implementation of the ISocialPlatform interface. Methods lacking an implementation
    /// or whose behavior is at variance with the standard are noted as such.
    /// </summary>
#pragma warning disable 0618 // Deprecated Unity APIs
    public class PlayGamesPlatform : ISocialPlatform
#pragma warning restore 0618
    {
        /// <summary>Singleton instance</summary>
        private static volatile PlayGamesPlatform sInstance = null;

        /// <summary>status of nearby connection initialization.</summary>
        private static volatile bool sNearbyInitializePending;

        /// <summary>Reference to the nearby client.</summary>
        /// <remarks>This is static since it can be used without using play game services.</remarks>
        private static volatile INearbyConnectionClient sNearbyConnectionClient;

        /// <summary>The local user.</summary>
        private PlayGamesLocalUser mLocalUser = null;

        /// <summary>Reference to the platform specific implementation.</summary>
        private IPlayGamesClient mClient = null;

        /// <summary>the default leaderboard we show on ShowLeaderboardUI</summary>
        private string mDefaultLbUi = null;

        /// <summary>the mapping table from alias to leaderboard/achievement id.</summary>
        private Dictionary<string, string> mIdMap = new Dictionary<string, string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="GooglePlayGames.PlayGamesPlatform"/> class.
        /// </summary>
        /// <param name="client">Implementation client to use for this instance.</param>
        internal PlayGamesPlatform(IPlayGamesClient client)
        {
            this.mClient = Misc.CheckNotNull(client);
            this.mLocalUser = new PlayGamesLocalUser(this);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GooglePlayGames.PlayGamesPlatform"/> class.
        /// </summary>
        private PlayGamesPlatform()
        {
            GooglePlayGames.OurUtils.Logger.d("Creating new PlayGamesPlatform");
            this.mLocalUser = new PlayGamesLocalUser(this);
        }

        /// <summary>
        /// Gets or sets a value indicating whether debug logs are enabled. This property
        /// may be set before calling <see cref="Activate" /> method.
        /// </summary>
        /// <returns>
        /// <c>true</c> if debug log enabled; otherwise, <c>false</c>.
        /// </returns>
        public static bool DebugLogEnabled
        {
            get { return GooglePlayGames.OurUtils.Logger.DebugLogEnabled; }

            set { GooglePlayGames.OurUtils.Logger.DebugLogEnabled = value; }
        }

        /// <summary>
        /// Gets the singleton instance of the Play Games platform.
        /// </summary>
        /// <returns>
        /// The instance.
        /// </returns>
        public static PlayGamesPlatform Instance
        {
            get
            {
                if (sInstance == null)
                {
                  OurUtils.Logger.d("Initializing the PlayGamesPlatform instance.");
                  sInstance =
                      new PlayGamesPlatform(PlayGamesClientFactory.GetPlatformPlayGamesClient());
                }

                return sInstance;
            }
        }

        /// <summary>
        /// Gets the nearby connection client.  NOTE: Can be null until the nearby client
        /// is initialized.  Call InitializeNearby to use callback to be notified when initialization
        /// is complete.
        /// </summary>
        /// <value>The nearby.</value>
        public static INearbyConnectionClient Nearby
        {
            get
            {
                if (sNearbyConnectionClient == null && !sNearbyInitializePending)
                {
                    sNearbyInitializePending = true;
                    InitializeNearby(null);
                }

                return sNearbyConnectionClient;
            }
        }

        /// <summary>Gets the saved game client object.</summary>
        /// <value>The saved game client.</value>
        public ISavedGameClient SavedGame
        {
            get { return mClient.GetSavedGameClient(); }
        }

        /// <summary>Gets the events client object.</summary>
        /// <value>The events client.</value>
        public IEventsClient Events
        {
            get { return mClient.GetEventsClient(); }
        }

        /// <summary>
        /// Gets the local user.
        /// </summary>
        /// <returns>
        /// The local user.
        /// </returns>
#pragma warning disable 0618 // Deprecated Unity APIs
        public ILocalUser localUser
#pragma warning restore 0618
        {
            get { return mLocalUser; }
        }

        /// <summary>
        /// Initializes the nearby connection platform.
        /// </summary>
        /// <remarks>This call initializes the nearby connection platform.  This
        /// is independent of the Play Game Services initialization.  Multiple
        /// calls to this method are ignored.
        /// </remarks>
        /// <param name="callback">Callback invoked when  complete.</param>
        public static void InitializeNearby(Action<INearbyConnectionClient> callback)
        {
            OurUtils.Logger.d("Calling InitializeNearby!");
            if (sNearbyConnectionClient == null)
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                NearbyConnectionClientFactory.Create(client => {
                    OurUtils.Logger.d("Nearby Client Created!!");
                    sNearbyConnectionClient = client;
                    if (callback != null) {
                        callback.Invoke(client);
                    }
                    else {
                        OurUtils.Logger.d("Initialize Nearby callback is null");
                    }
                });
#else
                sNearbyConnectionClient = new DummyNearbyConnectionClient();
                if (callback != null)
                {
                    callback.Invoke(sNearbyConnectionClient);
                }

#endif
            }
            else if (callback != null)
            {
                OurUtils.Logger.d("Nearby Already initialized: calling callback directly");
                callback.Invoke(sNearbyConnectionClient);
            }
            else
            {
                OurUtils.Logger.d("Nearby Already initialized");
            }
        }

        /// <summary>
        /// Activates the Play Games platform as the implementation of Social.Active.
        /// After calling this method, you can call methods on Social.Active. For
        /// example, <c>Social.Active.Authenticate()</c>.
        /// </summary>
        /// <returns>The singleton <see cref="PlayGamesPlatform" /> instance.</returns>
        public static PlayGamesPlatform Activate()
        {
            GooglePlayGames.OurUtils.Logger.d("Activating PlayGamesPlatform.");

#pragma warning disable 0618 // Deprecated Unity APIs
            Social.Active = PlayGamesPlatform.Instance;
#pragma warning restore 0618
            GooglePlayGames.OurUtils.Logger.d(
#pragma warning disable 0618 // Deprecated Unity APIs
                "PlayGamesPlatform activated: " + Social.Active);
#pragma warning restore 0618
            return PlayGamesPlatform.Instance;
        }

        /// <summary>
        /// Specifies that the ID <c>fromId</c> should be implicitly replaced by <c>toId</c>
        /// on any calls that take a leaderboard or achievement ID.
        /// </summary>
        /// <remarks> After a mapping is
        /// registered, you can use <c>fromId</c> instead of <c>toId</c> when making a call.
        /// For example, the following two snippets are equivalent:
        /// <code>
        /// ReportProgress("Cfiwjew894_AQ", 100.0, callback);
        /// </code>
        /// ...is equivalent to:
        /// <code>
        /// AddIdMapping("super-combo", "Cfiwjew894_AQ");
        /// ReportProgress("super-combo", 100.0, callback);
        /// </code>
        /// </remarks>
        /// <param name='fromId'>
        /// The identifier to map.
        /// </param>
        /// <param name='toId'>
        /// The identifier that <c>fromId</c> will be mapped to.
        /// </param>
        public void AddIdMapping(string fromId, string toId)
        {
            mIdMap[fromId] = toId;
        }

        /// <summary>
        /// Returns the result of the automatic sign-in attempt. Play Games SDK automatically
        /// prompts users to sign in when the game is started. This API is useful for understanding
        /// if your game has access to Play Games Services and should be used when your game is
        /// started in order to conditionally enable or disable your Play Games Services
        /// integration.
        /// </summary>
        /// <param name="callback">The callback to call when authentication finishes.</param>
        public void Authenticate(Action<SignInStatus> callback)
        {
            mClient.Authenticate(callback);
        }

        /// <summary>
        ///  Provided for compatibility with ISocialPlatform.
        /// </summary>
        /// <seealso cref="Authenticate(Action<bool>,bool)"/>
        /// <param name="unused">Unused parameter for this implementation.</param>
        /// <param name="callback">Callback invoked when complete.</param>
#pragma warning disable 0618 // Deprecated Unity APIs
        public void Authenticate(ILocalUser unused, Action<bool> callback)
#pragma warning restore 0618
        {
            Authenticate(status => callback(status == SignInStatus.Success));
        }

        /// <summary>
        ///  Provided for compatibility with ISocialPlatform.
        /// </summary>
        /// <seealso cref="Authenticate(Action<bool>,bool)"/>
        /// <param name="unused">Unused parameter for this implementation.</param>
        /// <param name="callback">Callback invoked when complete.</param>
#pragma warning disable 0618 // Deprecated Unity APIs
        public void Authenticate(ILocalUser unused, Action<bool, string> callback)
#pragma warning restore 0618
        {
            Authenticate(status => callback(status == SignInStatus.Success, status.ToString()));
        }

        /// <summary>
        /// Manually requests that your game performs sign in with Play Games Services.
        /// </summary>
        /// <remarks>
        /// Note that a sign-in attempt will be made automatically when your game's application
        /// started. For this reason most games will not need to manually request to perform sign-in
        /// unless the automatic sign-in attempt failed and your game requires access to Play Games
        /// Services.
        /// </remarks>
        /// <param name="callback"></param>
        public void ManuallyAuthenticate(Action<SignInStatus> callback) {
          mClient.ManuallyAuthenticate(callback);
        }

        /// <summary>
        /// Determines whether the user is authenticated.
        /// </summary>
        /// <returns>
        /// <c>true</c> if the user is authenticated; otherwise, <c>false</c>.
        /// </returns>
        public bool IsAuthenticated()
        {
            return mClient != null && mClient.IsAuthenticated();
        }

        /// <summary>
        /// Requests server-side access to Player Games Services for the currently signed in player.
        /// </summary>
        /// When requested an authorization code is returned that can be used by your game-server to
        /// exchange for an access token and conditionally a refresh token (when {@code
        /// forceRefreshToken} is true). The access token may then be used by your game-server to
        /// access the Play Games Services web APIs. This is commonly used to complete a sign-in flow
        /// by verifying the Play Games Services player id.
        ///
        /// <p>If {@code forceRefreshToken} is true, when exchanging the authorization code a refresh
        /// token will be returned in addition to the access token. The refresh token allows the
        /// game-server to request additional access tokens, allowing your game-server to continue
        /// accesses Play Games Services while the user is not actively playing your app. <remarks>
        ///
        /// </remarks>
        /// <param name="forceRefreshToken">If {@code true} when the returned authorization code is
        /// exchanged a refresh token will be included in addition to an access token.</param> <param
        /// name="callback"></param>
        public void RequestServerSideAccess(bool forceRefreshToken, Action<string> callback)
        {
            Misc.CheckNotNull(callback);

            if (!IsAuthenticated())
            {
                OurUtils.Logger.e("RequestServerSideAccess() can only be called after authentication.");
                InvokeCallbackOnGameThread(callback, null);
                return;
            }

            mClient.RequestServerSideAccess(forceRefreshToken, callback);
        }


        public void RequestRecallAccess(Action<RecallAccess> callback)
        {
            Misc.CheckNotNull(callback);

            mClient.RequestRecallAccessToken(callback);
        }

        /// <summary>
        /// Loads the users.
        /// </summary>
        /// <param name="userIds">User identifiers.</param>
        /// <param name="callback">Callback invoked when complete.</param>
#pragma warning disable 0618 // Deprecated Unity APIs
        public void LoadUsers(string[] userIds, Action<IUserProfile[]> callback)
#pragma warning restore 0618
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "GetUserId() can only be called after authentication.");
#pragma warning disable 0618 // Deprecated Unity APIs
                callback(new IUserProfile[0]);
#pragma warning restore 0618

                return;
            }

            mClient.LoadUsers(userIds, callback);
        }

        /// <summary>
        /// Returns the user's Google ID.
        /// </summary>
        /// <returns>
        /// The user's Google ID. No guarantees are made as to the meaning or format of
        /// this identifier except that it is unique to the user who is signed in.
        /// </returns>
        public string GetUserId()
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "GetUserId() can only be called after authentication.");
                return "0";
            }

            return mClient.GetUserId();
        }

        /// <summary>
        /// Gets the player stats.
        /// </summary>
        /// <param name="callback">Callback invoked when completed.</param>
        public void GetPlayerStats(Action<CommonStatusCodes, PlayerStats> callback)
        {
            if (mClient != null && mClient.IsAuthenticated())
            {
                mClient.GetPlayerStats(callback);
            }
            else
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "GetPlayerStats can only be called after authentication.");

                callback(CommonStatusCodes.SignInRequired, new PlayerStats());
            }
        }

        /// <summary>
        /// Returns the user's display name.
        /// </summary>
        /// <returns>
        /// The user display name (e.g. "Bruno Oliveira")
        /// </returns>
        public string GetUserDisplayName()
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "GetUserDisplayName can only be called after authentication.");
                return string.Empty;
            }

            return mClient.GetUserDisplayName();
        }

        /// <summary>
        /// Returns the user's avatar URL if they have one.
        /// </summary>
        /// <returns>
        /// The URL, or <code>null</code> if the user is not authenticated or does not have
        /// an avatar.
        /// </returns>
        public string GetUserImageUrl()
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "GetUserImageUrl can only be called after authentication.");
                return null;
            }

            return mClient.GetUserImageUrl();
        }

        /// <summary>
        /// Reports the progress of an achievement (reveal, unlock or increment). This method attempts
        /// to implement the expected behavior of ISocialPlatform.ReportProgress as closely as possible,
        /// as described below. Although this method works with incremental achievements for compatibility
        /// purposes, calling this method for incremental achievements is not recommended,
        /// since the Play Games API exposes incremental achievements in a very different way
        /// than the interface presented by ISocialPlatform.ReportProgress. The implementation of this
        /// method for incremental achievements attempts to produce the correct result, but may be
        /// imprecise. If possible, call <see cref="IncrementAchievement" /> instead.
        /// </summary>
        /// <param name='achievementID'>
        /// The ID of the achievement to unlock, reveal or increment. This can be a raw Google Play
        /// Games achievement ID (alphanumeric string), or an alias that was previously configured
        /// by a call to <see cref="AddIdMapping" />.
        /// </param>
        /// <param name='progress'>
        /// Progress of the achievement. If the achievement is standard (not incremental), then
        /// a progress of 0.0 will reveal the achievement and 100.0 will unlock it. Behavior of other
        /// values is undefined. If the achievement is incremental, then this value is interpreted
        /// as the total percentage of the achievement's progress that the player should have
        /// as a result of this call (regardless of the progress they had before). So if the
        /// player's previous progress was 30% and this call specifies 50.0, the new progress will
        /// be 50% (not 80%).
        /// </param>
        /// <param name='callback'>
        /// Callback that will be called to report the result of the operation: <c>true</c> on
        /// success, <c>false</c> otherwise.
        /// </param>
        public void ReportProgress(string achievementID, double progress, Action<bool> callback)
        {
            callback = ToOnGameThread(callback);
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "ReportProgress can only be called after authentication.");
                callback.Invoke(false);

                return;
            }

            // map ID, if it's in the dictionary
            GooglePlayGames.OurUtils.Logger.d("ReportProgress, " + achievementID + ", " + progress);
            achievementID = MapId(achievementID);

            // if progress is 0.0, we just want to reveal it
            if (progress < 0.000001)
            {
                GooglePlayGames.OurUtils.Logger.d(
                    "Progress 0.00 interpreted as request to reveal.");
                mClient.RevealAchievement(achievementID, callback);
                return;
            }

            // figure out if it's incremental
            bool isIncremental = false;
            int curSteps = 0, totalSteps = 0;

            mClient.LoadAchievements(ach =>
            {
                for (int i = 0; i < ach.Length; i++)
                {
                    if (ach[i].Id != null && ach[i].Id.Equals(achievementID))
                    {
                        isIncremental = ach[i].IsIncremental;
                        curSteps = ach[i].CurrentSteps;
                        totalSteps = ach[i].TotalSteps;
                        break;
                    }
                }


                GooglePlayGames.OurUtils.Logger.d("Achievement " + achievementID + " is " +
                    (isIncremental ? "INCREMENTAL" : "STANDARD"));

                if (isIncremental)
                {
                    GooglePlayGames.OurUtils.Logger.d("Progress " + progress +
                        " interpreted as percentage.");

                    // what's the current percentage?
                    double currentPercent = 0.0;
                    if (totalSteps > 0)
                    {
                        currentPercent = ((double) curSteps / (double) totalSteps) * 100.0;
                    }

                    // are we reporting completed?
                    if (progress >= 100.0)
                    {
                        // report 100% means asking to unlock the achievement.
                        GooglePlayGames.OurUtils.Logger.d(
                            "Progress " + progress + " interpreted as UNLOCK.");
                        mClient.UnlockAchievement(achievementID, callback);
                    }
                    else
                    {
                        // we are reporting a percentage. How many steps does that mean?
                        // note that we need to report the INCREMENT, not the new total.
                        int steps = (int) (progress / 100.0 * totalSteps);
                        int increment = steps - curSteps;
                        GooglePlayGames.OurUtils.Logger.d("Steps is " + steps + ", current is " +
                            curSteps + ". Increment is " + increment);
                        if (increment > 0)
                        {
                            mClient.IncrementAchievement(achievementID, increment, callback);
                        }
                    }
                }
                else
                {
                    // standard achievement
                    if (progress >= 100.0)
                    {
                        // unlock
                        GooglePlayGames.OurUtils.Logger.d(
                            "Progress " + progress + " interpreted as UNLOCK.");
                        mClient.UnlockAchievement(achievementID, callback);
                    }
                    else
                    {
                        // not enough progress to unlock, so we reveal it
                        GooglePlayGames.OurUtils.Logger.d(
                            "Progress " + progress + " not enough to unlock. Revealing.");
                        mClient.RevealAchievement(achievementID, callback);
                    }
                }
            });
        }

        /// <summary>
        /// Reveals the achievement specified by ID. Equivalent to calling
        /// <see cref="ReportProgress" /> with progress 0.0.
        /// </summary>
        /// <param name='achievementID'>
        /// Achievement identifier. This may be a raw Google Play Games achievement ID
        /// or an alias configured via <see cref="AddIdMapping" />.
        /// </param>
        /// <param name='callback'>
        /// Callback invoked when the operation completes. <c>true</c> indicates success,
        /// <c>false</c> indicates failure.
        /// </param>
        public void RevealAchievement(string achievementID, Action<bool> callback = null)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "RevealAchievement can only be called after authentication.");
                if (callback != null)
                {
                    callback.Invoke(false);
                }

                return;
            }

            // map ID, if it's in the dictionary
            GooglePlayGames.OurUtils.Logger.d("RevealAchievement: " + achievementID);
            achievementID = MapId(achievementID);
            mClient.RevealAchievement(achievementID, callback);
        }

        /// <summary>
        /// Unlocks the achievement specified by ID. Equivalent to calling
        /// <see cref="ReportProgress" /> with progress 100.0.
        /// </summary>
        /// <param name='achievementID'>
        /// Achievement identifier. This may be a raw Google Play Games achievement ID
        /// or an alias configured via <see cref="AddIdMapping" />.
        /// </param>
        /// <param name='callback'>
        /// Callback invoked when the operation completes. <c>true</c> indicates success,
        /// <c>false</c> indicates failure.
        /// </param>
        public void UnlockAchievement(string achievementID, Action<bool> callback = null)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "UnlockAchievement can only be called after authentication.");
                if (callback != null)
                {
                    callback.Invoke(false);
                }

                return;
            }

            // map ID, if it's in the dictionary
            GooglePlayGames.OurUtils.Logger.d("UnlockAchievement: " + achievementID);
            achievementID = MapId(achievementID);
            mClient.UnlockAchievement(achievementID, callback);
        }

        /// <summary>
        /// Increments the achievement specified by ID. This is only applicable to
        /// incremental achievements. For standard achievements, this call is equivalent
        /// to <see cref="UnlockAchievement" />. If the achievement is already revealed
        /// or unlocked, this call is ignored.
        /// </summary>
        /// <param name='achievementID'>
        /// Achievement identifier. This may be a raw Google Play Games achievement ID
        /// or an alias configured via <see cref="AddIdMapping" />.
        /// </param>
        /// <param name='steps'>
        /// The number of steps to increment the achievement by.
        /// </param>
        /// <param name='callback'>
        /// The callback to call to report the success or failure of the operation. The callback
        /// will be called with <c>true</c> to indicate success or <c>false</c> for failure.
        /// </param>
        public void IncrementAchievement(string achievementID, int steps, Action<bool> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "IncrementAchievement can only be called after authentication.");
                if (callback != null)
                {
                    callback.Invoke(false);
                }

                return;
            }

            // map ID, if it's in the dictionary
            GooglePlayGames.OurUtils.Logger.d(
                "IncrementAchievement: " + achievementID + ", steps " + steps);
            achievementID = MapId(achievementID);
            mClient.IncrementAchievement(achievementID, steps, callback);
        }

        /// <summary>
        /// Set an achievement to have at least the given number of steps completed.
        /// Calling this method with steps less than the current number of steps is
        /// ignored. If the number of steps is greater than the maximum number of steps,
        /// the achievement is automatically unlocked, and any further mutation operations
        /// are ignored.
        /// </summary>
        /// <param name='achievementID'>
        /// The ID of the achievement to increment. This can be a raw Google Play
        /// Games achievement ID (alphanumeric string), or an alias that was previously configured
        /// by a call to <see cref="AddIdMapping" />.
        /// </param>
        /// <param name='steps'>
        /// The number of steps to increment the achievement by.
        /// </param>
        /// <param name='callback'>
        /// The callback to call to report the success or failure of the operation. The callback
        /// will be called with <c>true</c> to indicate success or <c>false</c> for failure.
        /// </param>
        public void SetStepsAtLeast(string achievementID, int steps, Action<bool> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "SetStepsAtLeast can only be called after authentication.");
                if (callback != null)
                {
                    callback.Invoke(false);
                }

                return;
            }

            // map ID, if it's in the dictionary
            GooglePlayGames.OurUtils.Logger.d(
                "SetStepsAtLeast: " + achievementID + ", steps " + steps);
            achievementID = MapId(achievementID);
            mClient.SetStepsAtLeast(achievementID, steps, callback);
        }

        /// <summary>
        /// Loads the Achievement descriptions.
        /// </summary>
        /// <param name="callback">The callback to receive the descriptions</param>
#pragma warning disable 0618 // Deprecated Unity APIs
        public void LoadAchievementDescriptions(Action<IAchievementDescription[]> callback)
#pragma warning restore 0618
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "LoadAchievementDescriptions can only be called after authentication.");
                if (callback != null)
                {
                    callback.Invoke(null);
                }

                return;
            }

            mClient.LoadAchievements(ach =>
            {
#pragma warning disable 0618 // Deprecated Unity APIs
                IAchievementDescription[] data = new IAchievementDescription[ach.Length];
#pragma warning restore 0618
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = new PlayGamesAchievement(ach[i]);
                }

                callback.Invoke(data);
            });
        }

        /// <summary>
        /// Loads the achievement state for the current user.
        /// </summary>
        /// <param name="callback">The callback to receive the achievements</param>
#pragma warning disable 0618 // Deprecated Unity APIs
        public void LoadAchievements(Action<IAchievement[]> callback)
#pragma warning restore 0618
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e("LoadAchievements can only be called after authentication.");
                callback.Invoke(null);

                return;
            }

            mClient.LoadAchievements(ach =>
            {
#pragma warning disable 0618 // Deprecated Unity APIs
                IAchievement[] data = new IAchievement[ach.Length];
#pragma warning restore 0618
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = new PlayGamesAchievement(ach[i]);
                }

                callback.Invoke(data);
            });
        }

        /// <summary>
        /// Creates an achievement object which may be subsequently used to report an
        /// achievement.
        /// </summary>
        /// <returns>
        /// The achievement object.
        /// </returns>
#pragma warning disable 0618 // Deprecated Unity APIs
        public IAchievement CreateAchievement()
#pragma warning restore 0618
        {
            return new PlayGamesAchievement();
        }

        /// <summary>
        /// Reports a score to a leaderboard.
        /// </summary>
        /// <param name='score'>
        /// The score to report.
        /// </param>
        /// <param name='board'>
        /// The ID of the leaderboard on which the score is to be posted. This may be a raw
        /// Google Play Games leaderboard ID or an alias configured through a call to
        /// <see cref="AddIdMapping" />.
        /// </param>
        /// <param name='callback'>
        /// The callback to call to report the success or failure of the operation. The callback
        /// will be called with <c>true</c> to indicate success or <c>false</c> for failure.
        /// </param>
        public void ReportScore(long score, string board, Action<bool> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e("ReportScore can only be called after authentication.");
                if (callback != null)
                {
                    callback.Invoke(false);
                }

                return;
            }

            GooglePlayGames.OurUtils.Logger.d("ReportScore: score=" + score + ", board=" + board);
            string leaderboardId = MapId(board);
            mClient.SubmitScore(leaderboardId, score, callback);
        }

        /// <summary>
        /// Submits the score for the currently signed-in player
        /// to the leaderboard associated with a specific id
        /// and metadata (such as something the player did to earn the score).
        /// </summary>
        /// <param name="score">Score to report.</param>
        /// <param name="board">leaderboard id.</param>
        /// <param name="metadata">metadata about the score.</param>
        /// <param name="callback">Callback invoked upon completion.</param>
        public void ReportScore(long score, string board, string metadata, Action<bool> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e("ReportScore can only be called after authentication.");
                if (callback != null)
                {
                    callback.Invoke(false);
                }

                return;
            }

            GooglePlayGames.OurUtils.Logger.d("ReportScore: score=" + score +
                                              ", board=" + board +
                                              " metadata=" + metadata);
            string leaderboardId = MapId(board);
            mClient.SubmitScore(leaderboardId, score, metadata, callback);
        }

        /// <summary>
        /// Loads the scores relative the player.
        /// </summary>
        /// <remarks>This returns the 25
        /// (which is the max results returned by the SDK per call) scores
        /// that are around the player's score on the Public, all time leaderboard.
        /// Use the overloaded methods which are specific to GPGS to modify these
        /// parameters.
        /// </remarks>
        /// <param name="leaderboardId">Leaderboard Id</param>
        /// <param name="callback">Callback to invoke when completed.</param>
#pragma warning disable 0618 // Deprecated Unity APIs
        public void LoadScores(string leaderboardId, Action<IScore[]> callback)
#pragma warning restore 0618
        {
            LoadScores(
                leaderboardId,
                LeaderboardStart.PlayerCentered,
                mClient.LeaderboardMaxResults(),
                LeaderboardCollection.Public,
#pragma warning disable 0618 // Deprecated Unity APIs
                LeaderboardTimeSpan.AllTime,
#pragma warning restore 0618
                (scoreData) => callback(scoreData.Scores));
        }

        /// <summary>
        /// Loads the scores using the provided parameters. This call may fail when trying to load friends with
        /// ResponseCode.ResolutionRequired if the user has not share the friends list with the game. In this case, use
        /// AskForLoadFriendsResolution to request access.
        /// </summary>
        /// <param name="leaderboardId">Leaderboard identifier.</param>
        /// <param name="start">Start either top scores, or player centered.</param>
        /// <param name="rowCount">Row count. the number of rows to return.</param>
        /// <param name="collection">Collection. social or public</param>
        /// <param name="timeSpan">Time span. daily, weekly, all-time</param>
        /// <param name="callback">Callback to invoke when completed.</param>
#pragma warning disable 0618 // Deprecated Unity APIs
        public void LoadScores(
            string leaderboardId,
            LeaderboardStart start,
#pragma warning restore 0618
            int rowCount,
            LeaderboardCollection collection,
#pragma warning disable 0618 // Deprecated Unity APIs
            LeaderboardTimeSpan timeSpan,
#pragma warning restore 0618
            Action<LeaderboardScoreData> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e("LoadScores can only be called after authentication.");
                callback(new LeaderboardScoreData(
                    leaderboardId,
                    ResponseStatus.NotAuthorized));
                return;
            }

            leaderboardId = MapId(leaderboardId);
            mClient.LoadScores(leaderboardId, start, rowCount, collection, timeSpan, callback);
        }

        /// <summary>
        /// Loads the more scores. Use the LeaderboardScoreData returned from a previous call
        /// to LoadScores.
        /// </summary>
        /// <param name="token">Token used to identify the next page of scores.</param>
        /// <param name="rowCount">Row count.</param>
        /// <param name="callback">Callback.</param>
        public void LoadMoreScores(
            ScorePageToken token,
            int rowCount,
            Action<LeaderboardScoreData> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e("LoadMoreScores can only be called after authentication.");
                callback(new LeaderboardScoreData(
                    token.LeaderboardId,
                    ResponseStatus.NotAuthorized));
                return;
            }

            mClient.LoadMoreScores(token, rowCount, callback);
        }

        /// <summary>
        /// Creates a leaderboard object. This can be used to interact with leaderboards.
        /// </summary>
        /// <returns>
        /// The leaderboard object.
        /// </returns>
#pragma warning disable 0618 // Deprecated Unity APIs
        public ILeaderboard CreateLeaderboard()
#pragma warning restore 0618
        {
            return new PlayGamesLeaderboard(mDefaultLbUi);
        }

        /// <summary>
        /// Shows the standard achievements UI.
        /// </summary>
        public void ShowAchievementsUI()
        {
            ShowAchievementsUI(null);
        }

        /// <summary>
        /// Shows the standard achievements UI.
        /// </summary>
        /// <param name="callback">Callback invoked when the UI is dismissed.</param>
        public void ShowAchievementsUI(Action<UIStatus> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e("ShowAchievementsUI can only be called after authentication.");
                return;
            }

            mClient.ShowAchievementsUI(callback);
        }

        /// <summary>
        /// Shows the standard leaderboard UI for the given leaderboard ID.
        /// </summary>
        /// <remarks> If leaderboardId is null, it shows the list of all leaderboards.
        /// </remarks>
        public void ShowLeaderboardUI()
        {
            ShowLeaderboardUI(null);
        }

        /// <summary>
        /// Shows the standard leaderboard UI for the given leaderboard ID.
        /// </summary>
        /// <remarks> If leaderboardId is null, it shows the list of all leaderboards.
        /// </remarks>
        /// <param name='leaderboardId'>
        /// Leaderboard identifier. This may be a raw Google Play Games leaderboard ID
        /// or an alias configured via <see cref="AddIdMapping" />. If null, shows the list
        /// of all leaderboards.
        /// </param>
        public void ShowLeaderboardUI(string leaderboardId)
        {
            ShowLeaderboardUI(leaderboardId, null);
        }

        /// <summary>
        /// Shows the standard leaderboard UI for the given leaderboard ID.
        /// </summary>
        /// <remarks> If leaderboardId is null, it shows the list of all leaderboards.
        /// </remarks>
        /// <param name='leaderboardId'>
        /// Leaderboard identifier. This may be a raw Google Play Games leaderboard ID
        /// or an alias configured via <see cref="AddIdMapping" />. If null, shows the list
        /// of all leaderboards.
        /// </param>
        /// <param name="callback">Callback invoked when the UI is dismissed.</param>
        public void ShowLeaderboardUI(string leaderboardId, Action<UIStatus> callback)
        {
#pragma warning disable 0618 // Deprecated Unity APIs
            ShowLeaderboardUI(leaderboardId, LeaderboardTimeSpan.AllTime, callback);
#pragma warning restore 0618
        }

        /// <summary>
        /// Shows the standard leaderboard UI for the given leaderboard ID and time span.
        /// </summary>
        /// <remarks> If leaderboardId is null, it shows the list of all leaderboards.
        /// </remarks>
        /// <param name='leaderboardId'>
        /// Leaderboard identifier. This may be a raw Google Play Games leaderboard ID
        /// or an alias configured via <see cref="AddIdMapping" />. If null, shows the list
        /// of all leaderboards.
        /// </param>
        /// <param name="span">The time span to display</param>
        /// <param name="callback">Callback invoked when the UI is dismissed.</param>
#pragma warning disable 0618 // Deprecated Unity APIs
        public void ShowLeaderboardUI(
            string leaderboardId,
            LeaderboardTimeSpan span,
#pragma warning restore 0618
            Action<UIStatus> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e("ShowLeaderboardUI can only be called after authentication.");
                return;
            }

            string lbId = leaderboardId == null ? null : MapId(leaderboardId);
#pragma warning disable 0618 // Deprecated Unity APIs
            mClient.ShowLeaderboardUI(lbId, span, callback);
#pragma warning restore 0618
        }

        /// <summary>
        /// Sets the default leaderboard for the leaderboard UI.
        /// </summary>
        /// <remarks> After calling this method, subsequent calls to <see cref="ShowLeaderboardUI" />
        /// without a leaderboard ID parameter will show the specified leaderboard instead
        /// of the list of all leaderboards.
        /// </remarks>
        /// <param name='lbid'>
        /// The ID of the leaderboard to show by default. This may be a raw Google Play Games
        /// leaderboard ID or an alias configured via <see cref="AddIdMapping" />.
        /// </param>
        public void SetDefaultLeaderboardForUI(string lbid)
        {
            GooglePlayGames.OurUtils.Logger.d("SetDefaultLeaderboardForUI: " + lbid);
            if (lbid != null)
            {
                lbid = MapId(lbid);
            }

            mDefaultLbUi = lbid;
        }

        /// <summary>
        /// Loads the friends of the authenticated user.
        /// </summary>
        /// <param name="user">The user to load friends for.</param>
        /// <param name="callback">Callback invoked when complete.</param>
#pragma warning disable 0618 // Deprecated Unity APIs
        public void LoadFriends(ILocalUser user, Action<bool> callback)
#pragma warning restore 0618
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e("LoadScores can only be called after authentication.");
                callback(false);
                return;
            }

            mClient.LoadFriends(callback);
        }

        /// <summary>
        /// Loads the scores for the specified leaderboard.
        /// </summary>
        /// <param name="board">The leaderboard to load scores for.</param>
        /// <param name="callback">Callback invoked when complete.</param>
#pragma warning disable 0618 // Deprecated Unity APIs
        public void LoadScores(ILeaderboard board, Action<bool> callback)
#pragma warning restore 0618
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e("LoadScores can only be called after authentication.");
                callback(false);
                return;
            }

            callback = ToOnGameThread(callback);
#pragma warning disable 0618 // Deprecated Unity APIs
            PlayGamesLeaderboard pgBoard = board as PlayGamesLeaderboard;
#pragma warning restore 0618
            if (pgBoard == null)
            {
                GooglePlayGames.OurUtils.Logger.e("LoadScores can only be called with PlayGamesLeaderboard object");
                callback(false);
                return;
            }

            LeaderboardCollection collection = LeaderboardCollection.Public;
#pragma warning disable 0618 // Deprecated Unity APIs
            switch (board.userScope)
#pragma warning restore 0618
            {
#pragma warning disable 0618 // Deprecated Unity APIs
                case UserScope.FriendsOnly:
#pragma warning restore 0618
                    collection = LeaderboardCollection.Social;
                    break;
#pragma warning disable 0618 // Deprecated Unity APIs
                case UserScope.Global:
#pragma warning restore 0618
                    collection = LeaderboardCollection.Public;
                    break;
                default:
#pragma warning disable 0618 // Deprecated Unity APIs
                    GooglePlayGames.OurUtils.Logger.e("UserScope not supported: " + board.userScope);
#pragma warning restore 0618
                    callback(false);
                    return;
            }

            pgBoard.loading = true;
#pragma warning disable 0618 // Deprecated Unity APIs
            LoadScores(
                board.id,
                board.range.from > 1 ? LeaderboardStart.TopScores : LeaderboardStart.PlayerCentered,
                board.range.count,
                collection,
                ToLeaderboardTimeSpan(board.timeScope), // Fixed: Use helper method
                (scoreData) => HandleLoadingScores(pgBoard, scoreData, callback));
#pragma warning restore 0618
        }

        /// <summary>
        /// Returns whether the specified leaderboard is currently loading.
        /// </summary>
        /// <returns><c>true</c> if loading; otherwise, <c>false</c>.</returns>
        /// <param name="board">Board.</param>
#pragma warning disable 0618 // Deprecated Unity APIs
        public bool GetLoading(ILeaderboard board)
#pragma warning restore 0618
        {
            if (board != null && board is PlayGamesLeaderboard)
            {
                return ((PlayGamesLeaderboard) board).loading;
            }

            return false;
        }

        /// <summary>
        /// Shows the player profile comparison UI for the specified player ID.
        /// </summary>
        /// <param name="userId">The ID of the player to show.</param>
        /// <param name="otherPlayerInGameName">The name the other player is using in the game.</param>
        /// <param name="currentPlayerInGameName">The name the current player is using in the game.</param>
        /// <param name="callback">Callback invoked when the UI is dismissed.</param>
        public void ShowCompareProfileWithAlternativeNameHintsUI(string userId,
            string otherPlayerInGameName,
            string currentPlayerInGameName,
            Action<UIStatus> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "ShowCompareProfileWithAlternativeNameHintsUI can only be called after authentication.");
                return;
            }

            mClient.ShowCompareProfileWithAlternativeNameHintsUI(userId, otherPlayerInGameName,
                currentPlayerInGameName, callback);
        }

        /// <summary>
        /// Gets the visibility of the friends list.
        /// </summary>
        /// <param name="forceReload">If true, forces a reload of the data.</param>
        /// <param name="callback">Callback invoked when complete.</param>
        public void GetFriendsListVisibility(bool forceReload,
            Action<FriendsListVisibilityStatus> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "GetFriendsListVisibility can only be called after authentication.");
                callback(FriendsListVisibilityStatus.NotAuthorized);
                return;
            }

            mClient.GetFriendsListVisibility(forceReload, callback);
        }

        /// <summary>
        /// Asks the user for permission to load the friends list.
        /// </summary>
        /// <param name="callback">Callback invoked when the UI is dismissed.</param>
        public void AskForLoadFriendsResolution(Action<UIStatus> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "AskForLoadFriendsResolution can only be called after authentication.");
                callback(UIStatus.NotAuthorized);
                return;
            }

            mClient.AskForLoadFriendsResolution(callback);
        }

        /// <summary>
        /// Gets the last load friends status.
        /// </summary>
        /// <returns>The last load friends status.</returns>
        public LoadFriendsStatus GetLastLoadFriendsStatus()
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "GetLastLoadFriendsStatus can only be called after authentication.");
                return LoadFriendsStatus.NotAuthorized;
            }

            return mClient.GetLastLoadFriendsStatus();
        }

        /// <summary>
        /// Loads the friends list.
        /// </summary>
        /// <param name="pageSize">Page size.</param>
        /// <param name="forceReload">If set to <c>true</c> force reload.</param>
        /// <param name="callback">Callback.</param>
        public void LoadFriends(int pageSize, bool forceReload,
            Action<LoadFriendsStatus> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "LoadFriends can only be called after authentication.");
                callback(LoadFriendsStatus.NotAuthorized);
                return;
            }

            mClient.LoadFriends(pageSize, forceReload, callback);
        }

        /// <summary>
        /// Loads more friends.
        /// </summary>
        /// <param name="pageSize">Page size.</param>
        /// <param name="callback">Callback.</param>
        public void LoadMoreFriends(int pageSize, Action<LoadFriendsStatus> callback)
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.e(
                    "LoadMoreFriends can only be called after authentication.");
                callback(LoadFriendsStatus.NotAuthorized);
                return;
            }

            mClient.LoadMoreFriends(pageSize, callback);
        }

        /// <summary>
        /// Handles the processing of scores during loading.
        /// </summary>
        /// <param name="board">leaderboard being loaded</param>
        /// <param name="scoreData">Score data.</param>
        /// <param name="callback">Callback invoked when complete.</param>
        internal void HandleLoadingScores(
            PlayGamesLeaderboard board,
            LeaderboardScoreData scoreData,
            Action<bool> callback)
        {
            bool ok = board.SetFromData(scoreData);
            if (ok && !board.HasAllScores() && scoreData.NextPageToken != null)
            {
#pragma warning disable 0618 // Deprecated Unity APIs
                int rowCount = board.range.count - board.ScoreCount;
#pragma warning restore 0618

                // need to load more scores
                mClient.LoadMoreScores(
                    scoreData.NextPageToken,
                    rowCount,
                    (nextScoreData) =>
                        HandleLoadingScores(board, nextScoreData, callback));
            }
            else
            {
                callback(ok);
            }
        }

        /// <summary>
        /// Internal implmentation of getFriends.Gets the friends.
        /// </summary>
        /// <returns>The friends.</returns>
#pragma warning disable 0618 // Deprecated Unity APIs
        internal IUserProfile[] GetFriends()
#pragma warning restore 0618
        {
            if (!IsAuthenticated())
            {
                GooglePlayGames.OurUtils.Logger.d("Cannot get friends when not authenticated!");
#pragma warning disable 0618 // Deprecated Unity APIs
                return new IUserProfile[0];
#pragma warning restore 0618
            }

            return mClient.GetFriends();
        }

        /// <summary>
        /// Maps the alias to the identifier.
        /// </summary>
        /// <remarks>This maps an aliased ID to the actual id.  The intent of
        /// this method is to allow easy to read constants to be used instead of
        /// the generated ids.
        /// </remarks>
        /// <returns>The identifier, or null if not found.</returns>
        /// <param name="id">Alias to map</param>
        private string MapId(string id)
        {
            if (id == null)
            {
                return null;
            }

            if (mIdMap.ContainsKey(id))
            {
                string result = mIdMap[id];
                GooglePlayGames.OurUtils.Logger.d("Mapping alias " + id + " to ID " + result);
                return result;
            }

            return id;
        }

        private static void InvokeCallbackOnGameThread<T>(Action<T> callback, T data)
        {
            if (callback == null)
            {
                return;
            }

            PlayGamesHelperObject.RunOnGameThread(() => callback(data));
        }

        private static Action<T> ToOnGameThread<T>(Action<T> toConvert)
        {
            if (toConvert == null)
            {
                return delegate { };
            }

            return (val) => InvokeCallbackOnGameThread(toConvert, val);
        }

        // Helper to convert TimeScope
#pragma warning disable 0618 // Deprecated Unity APIs
        private static GooglePlayGames.BasicApi.LeaderboardTimeSpan ToLeaderboardTimeSpan (UnityEngine.SocialPlatforms.TimeScope scope)
#pragma warning restore 0618
        {
#pragma warning disable 0618 // Deprecated Unity APIs
            switch (scope)
            {
                case UnityEngine.SocialPlatforms.TimeScope.Week:
                    return GooglePlayGames.BasicApi.LeaderboardTimeSpan.Weekly;
                case UnityEngine.SocialPlatforms.TimeScope.AllTime:
                default:
                    return GooglePlayGames.BasicApi.LeaderboardTimeSpan.AllTime;
            }
#pragma warning restore 0618
        }
    }
}
#endif
