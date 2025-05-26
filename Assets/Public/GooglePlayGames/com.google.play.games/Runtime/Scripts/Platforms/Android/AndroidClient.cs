// <copyright file="NativeClient.cs" company="Google Inc.">
// Copyright (C) 2014 Google Inc.  All Rights Reserved.
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
#pragma warning disable 0642 // Possible mistaken empty statement

namespace GooglePlayGames.Android
{
    using GooglePlayGames.BasicApi;
    using GooglePlayGames.BasicApi.Events;
    using GooglePlayGames.BasicApi.SavedGame;
    using GooglePlayGames.OurUtils;
    using System;
    using UnityEngine;
    using UnityEngine.SocialPlatforms;

    public class AndroidClient : IPlayGamesClient
    {
        private enum AuthState
        {
            Unauthenticated,
            Authenticated
        }

        private readonly object GameServicesLock = new object();
        private readonly object AuthStateLock = new object();
        private readonly static String PlayGamesSdkClassName =
          "com.google.android.gms.games.PlayGamesSdk";

        private volatile ISavedGameClient mSavedGameClient;
        private volatile IEventsClient mEventsClient;
        private volatile Player mUser = null;
        private volatile AuthState mAuthState = AuthState.Unauthenticated;
#pragma warning disable 0618 // Deprecated Unity APIs
        private IUserProfile[] mFriends = new IUserProfile[0];
#pragma warning restore 0618
        private LoadFriendsStatus mLastLoadFriendsStatus = LoadFriendsStatus.Unknown;

        AndroidJavaClass mGamesClass = new AndroidJavaClass("com.google.android.gms.games.PlayGames");
        private static string TasksClassName = "com.google.android.gms.tasks.Tasks";

        private AndroidJavaObject mFriendsResolutionException = null;

        private readonly int mLeaderboardMaxResults = 25; // can be from 1 to 25

        private readonly int mFriendsMaxResults = 200; // the maximum load friends page size

        internal AndroidClient()
        {
            PlayGamesHelperObject.CreateObject();
            InitializeSdk();
        }

        private static void InitializeSdk() {
            using (var playGamesSdkClass = new AndroidJavaClass(PlayGamesSdkClassName)) {
                playGamesSdkClass.CallStatic("initialize", AndroidHelperFragment.GetActivity());
            }
        }

        public void Authenticate(Action<SignInStatus> callback)
        {
            Authenticate( /* isAutoSignIn= */ true, callback);
        }

        public void ManuallyAuthenticate(Action<SignInStatus> callback)
        {
            Authenticate( /* isAutoSignIn= */ false, callback);
        }

        private void Authenticate(bool isAutoSignIn, Action<SignInStatus> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            lock (AuthStateLock)
            {
                // If the user is already authenticated, just fire the callback, we don't need
                // any additional work.
                if (mAuthState == AuthState.Authenticated)
                {
                    OurUtils.Logger.d("Already authenticated.");
                    InvokeCallbackOnGameThread(callback, SignInStatus.Success);
                    return;
                }
            }

            string methodName = isAutoSignIn ? "isAuthenticated" : "signIn";

            OurUtils.Logger.d("Starting Auth using the method " + methodName);
            using (var client = getGamesSignInClient())
            using (
                var task = client.Call<AndroidJavaObject>(methodName))
            {
                AndroidTaskUtils.AddOnSuccessListener<AndroidJavaObject>(task, authenticationResult =>
                {
                    bool isAuthenticated = authenticationResult.Call<bool>("isAuthenticated");
                    SignInOnResult(isAuthenticated, callback);
                });

                AndroidTaskUtils.AddOnFailureListener(task, exception =>
                {
                    OurUtils.Logger.e("Authentication failed - " + exception.Call<string>("toString"));
                    callback(SignInStatus.InternalError);
                });
            }
        }

        private void SignInOnResult(bool isAuthenticated, Action<SignInStatus> callback)
        {
            if (isAuthenticated)
            {
                using (var signInTasks = new AndroidJavaObject("java.util.ArrayList"))
                {
                    AndroidJavaObject taskGetPlayer =
                        getPlayersClient().Call<AndroidJavaObject>("getCurrentPlayer");
                    signInTasks.Call<bool>("add", taskGetPlayer);

                    using (var tasks = new AndroidJavaClass(TasksClassName))
                    using (var allTask = tasks.CallStatic<AndroidJavaObject>("whenAll", signInTasks))
                    {
                        AndroidTaskUtils.AddOnCompleteListener<AndroidJavaObject>(
                            allTask,
                            completeTask =>
                            {
                                if (completeTask.Call<bool>("isSuccessful"))
                                {
                                    using (var resultObject = taskGetPlayer.Call<AndroidJavaObject>("getResult"))
                                    {
                                        mUser = AndroidJavaConverter.ToPlayer(resultObject);
                                    }

                                    lock (GameServicesLock)
                                    {
                                        mSavedGameClient = new AndroidSavedGameClient(this);
                                        mEventsClient = new AndroidEventsClient();
                                    }

                                    mAuthState = AuthState.Authenticated;
                                    InvokeCallbackOnGameThread(callback, SignInStatus.Success);
                                    OurUtils.Logger.d("Authentication succeeded");
                                    LoadAchievements(ignore => { });
                                }
                                else
                                {
                                    if (completeTask.Call<bool>("isCanceled"))
                                    {
                                        InvokeCallbackOnGameThread(callback, SignInStatus.Canceled);
                                        return;
                                    }

                                    using (var exception = completeTask.Call<AndroidJavaObject>("getException"))
                                    {
                                        OurUtils.Logger.e(
                                            "Authentication failed - " + exception.Call<string>("toString"));
                                        InvokeCallbackOnGameThread(callback, SignInStatus.InternalError);
                                    }
                                }
                            }
                        );
                    }
                }
            }
            else
            {
                lock (AuthStateLock)
                {
                    OurUtils.Logger.e("Returning an error code.");
                    InvokeCallbackOnGameThread(callback, SignInStatus.Canceled);
                }
            }
        }

        public void RequestServerSideAccess(bool forceRefreshToken, Action<string> callback)
        {
            callback = AsOnGameThreadCallback(callback);

            if (!GameInfo.WebClientIdInitialized())
            {
                throw new InvalidOperationException("Requesting server side access requires web " +
                                                    "client id to be configured.");
            }

            using (var client = getGamesSignInClient())
            using (var task = client.Call<AndroidJavaObject>("requestServerSideAccess",
                GameInfo.WebClientId, forceRefreshToken))
            {
                AndroidTaskUtils.AddOnSuccessListener<string>(
                    task,
                    authCode => callback(authCode)
                );

                AndroidTaskUtils.AddOnFailureListener(task, exception =>
                {
                    OurUtils.Logger.e("Requesting server side access task failed - " +
                                      exception.Call<string>("toString"));
                    callback(null);
                });
            }
        }

        public void RequestRecallAccessToken(Action<RecallAccess> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            using (var client = getRecallClient())
            using (var task = client.Call<AndroidJavaObject>("requestRecallAccess"))
            {
                AndroidTaskUtils.AddOnSuccessListener<AndroidJavaObject>(
                    task,
                    recallAccess => {
                        var sessionId = recallAccess.Call<string>("getSessionId");
                        callback(new RecallAccess(sessionId));
                    }
                );

                AndroidTaskUtils.AddOnFailureListener(task, exception =>
                {
                    OurUtils.Logger.e("Requesting Recall access task failed - " +
                                      exception.Call<string>("toString"));
                    callback(null);
                });
            }
        }

        private static Action<T> AsOnGameThreadCallback<T>(Action<T> callback)
        {
            if (callback == null)
            {
                return delegate { };
            }

            return result => InvokeCallbackOnGameThread(callback, result);
        }

        private static void InvokeCallbackOnGameThread(Action callback)
        {
            if (callback == null)
            {
                return;
            }

            PlayGamesHelperObject.RunOnGameThread(() => { callback(); });
        }

        private static void InvokeCallbackOnGameThread<T>(Action<T> callback, T data)
        {
            if (callback == null)
            {
                return;
            }

            PlayGamesHelperObject.RunOnGameThread(() => { callback(data); });
        }


        private static Action<T1, T2> AsOnGameThreadCallback<T1, T2>(
            Action<T1, T2> toInvokeOnGameThread)
        {
            return (result1, result2) =>
            {
                if (toInvokeOnGameThread == null)
                {
                    return;
                }

                PlayGamesHelperObject.RunOnGameThread(() => toInvokeOnGameThread(result1, result2));
            };
        }

        private static void InvokeCallbackOnGameThread<T1, T2>(Action<T1, T2> callback, T1 t1, T2 t2)
        {
            if (callback == null)
            {
                return;
            }

            PlayGamesHelperObject.RunOnGameThread(() => { callback(t1, t2); });
        }

        public bool IsAuthenticated()
        {
            lock (AuthStateLock)
            {
                return mAuthState == AuthState.Authenticated;
            }
        }

        public void LoadFriends(Action<bool> callback)
        {
            LoadAllFriends(mFriendsMaxResults, /* forceReload= */ false, /* loadMore= */ false, callback);
        }

        private void LoadAllFriends(int pageSize, bool forceReload, bool loadMore,
            Action<bool> callback)
        {
            LoadFriendsPaginated(pageSize, loadMore, forceReload, result =>
            {
                mLastLoadFriendsStatus = result;
                switch (result)
                {
                    case LoadFriendsStatus.Completed:
                        InvokeCallbackOnGameThread(callback, true);
                        break;
                    case LoadFriendsStatus.LoadMore:
                        // There are more friends to load.
                        LoadAllFriends(pageSize, /* forceReload= */ false, /* loadMore= */ true, callback);
                        break;
                    case LoadFriendsStatus.ResolutionRequired:
                    case LoadFriendsStatus.InternalError:
                    case LoadFriendsStatus.NotAuthorized:
                        InvokeCallbackOnGameThread(callback, false);
                        break;
                    default:
                        GooglePlayGames.OurUtils.Logger.d("There was an error when loading friends." + result);
                        InvokeCallbackOnGameThread(callback, false);
                        break;
                }
            });
        }

        public void LoadFriends(int pageSize, bool forceReload,
            Action<LoadFriendsStatus> callback)
        {
            LoadFriendsPaginated(pageSize, /* isLoadMore= */ false, /* forceReload= */ forceReload,
                callback);
        }

        public void LoadMoreFriends(int pageSize, Action<LoadFriendsStatus> callback)
        {
            LoadFriendsPaginated(pageSize, /* isLoadMore= */ true, /* forceReload= */ false,
                callback);
        }

        private void LoadFriendsPaginated(int pageSize, bool isLoadMore, bool forceReload,
            Action<LoadFriendsStatus> callback)
        {
            mFriendsResolutionException = null;
            using (var playersClient = getPlayersClient())
            using (var task = isLoadMore
                ? playersClient.Call<AndroidJavaObject>("loadMoreFriends", pageSize)
                : playersClient.Call<AndroidJavaObject>("loadFriends", pageSize,
                    forceReload))
            {
                AndroidTaskUtils.AddOnSuccessListener<AndroidJavaObject>(
                    task, annotatedData =>
                    {
                        using (var playersBuffer = annotatedData.Call<AndroidJavaObject>("get"))
                        {
                            AndroidJavaObject metadata = playersBuffer.Call<AndroidJavaObject>("getMetadata");
                            var areMoreFriendsToLoad = metadata != null &&
                                                       metadata.Call<AndroidJavaObject>("getString",
                                                           "next_page_token") != null;
#pragma warning disable 0618 // Deprecated Unity APIs
                            mFriends = AndroidJavaConverter.playersBufferToArray(playersBuffer);
#pragma warning restore 0618
                            mLastLoadFriendsStatus = areMoreFriendsToLoad
                                ? LoadFriendsStatus.LoadMore
                                : LoadFriendsStatus.Completed;
                            InvokeCallbackOnGameThread(callback, mLastLoadFriendsStatus);
                        }
                    });
                AndroidTaskUtils.AddOnFailureListener(task, exception =>
                {
                    AndroidHelperFragment.IsResolutionRequired(exception, resolutionRequired =>
                    {
                        if (resolutionRequired)
                        {
                            mFriendsResolutionException =
                                exception.Call<AndroidJavaObject>("getResolution");
                            mLastLoadFriendsStatus = LoadFriendsStatus.ResolutionRequired;
#pragma warning disable 0618 // Deprecated Unity APIs
                            mFriends = new IUserProfile[0];
#pragma warning restore 0618
                            InvokeCallbackOnGameThread(callback, LoadFriendsStatus.ResolutionRequired);
                        }
                        else
                        {
                            mFriendsResolutionException = null;

                            if (IsApiException(exception))
                            {
                                var statusCode = exception.Call<int>("getStatusCode");
                                if (statusCode == /* GamesClientStatusCodes.NETWORK_ERROR_NO_DATA */ 26504)
                                {
                                    mLastLoadFriendsStatus = LoadFriendsStatus.NetworkError;
                                    InvokeCallbackOnGameThread(callback, LoadFriendsStatus.NetworkError);
                                    return;
                                }
                            }

                            mLastLoadFriendsStatus = LoadFriendsStatus.InternalError;
                            OurUtils.Logger.e("LoadFriends failed: " +
                                exception.Call<string>("toString"));
                            InvokeCallbackOnGameThread(callback, LoadFriendsStatus.InternalError);
                        }
                    });
                    return;
                });
            }
        }

        private static bool IsApiException(AndroidJavaObject exception) {
            var exceptionClassName = exception.Call<AndroidJavaObject>("getClass")
                .Call<String>("getName");
            return exceptionClassName == "com.google.android.gms.common.api.ApiException";
        }

        public LoadFriendsStatus GetLastLoadFriendsStatus()
        {
            return mLastLoadFriendsStatus;
        }

        public void AskForLoadFriendsResolution(Action<UIStatus> callback)
        {
            if (mFriendsResolutionException == null)
            {
                GooglePlayGames.OurUtils.Logger.d("The developer asked for access to the friends " +
                                                  "list but there is no intent to trigger the UI. This may be because the user " +
                                                  "has granted access already or the game has not called loadFriends() before.");
                using (var playersClient = getPlayersClient())
                using (
                    var task = playersClient.Call<AndroidJavaObject>("loadFriends", /* pageSize= */ 1,
                        /* forceReload= */ false))
                {
                    AndroidTaskUtils.AddOnSuccessListener<AndroidJavaObject>(
                        task, annotatedData => { InvokeCallbackOnGameThread(callback, UIStatus.Valid); });
                    AndroidTaskUtils.AddOnFailureListener(task, exception =>
                    {
                        AndroidHelperFragment.IsResolutionRequired(exception, resolutionRequired =>
                        {
                            if (resolutionRequired)
                            {
                                mFriendsResolutionException =
                                    exception.Call<AndroidJavaObject>("getResolution");
                                AndroidHelperFragment.AskForLoadFriendsResolution( // Fixed: Call correct helper method
                                    mFriendsResolutionException, AsOnGameThreadCallback(callback));
                                return;
                            }

                            if (IsApiException(exception))
                            {
                                var statusCode = exception.Call<int>("getStatusCode");
                                if (statusCode ==
                                    /* GamesClientStatusCodes.NETWORK_ERROR_NO_DATA */ 26504)
                                {
                                    InvokeCallbackOnGameThread(callback, UIStatus.NetworkError);
                                    return;
                                }
                            }

                            OurUtils.Logger.e("LoadFriends failed: " +
                                exception.Call<string>("toString"));
                            InvokeCallbackOnGameThread(callback, UIStatus.InternalError);
                        });
                    });
                }
            }
            else
            {
                AndroidHelperFragment.AskForLoadFriendsResolution(mFriendsResolutionException, // Fixed: Call correct helper method
                    AsOnGameThreadCallback(callback));
            }
        }

        public void ShowCompareProfileWithAlternativeNameHintsUI(string playerId,
            string otherPlayerInGameName,
            string currentPlayerInGameName,
            Action<UIStatus> callback)
        {
            AndroidHelperFragment.ShowCompareProfileWithAlternativeNameHintsUI( // Fixed: Call correct helper method
                playerId, otherPlayerInGameName, currentPlayerInGameName,
                AsOnGameThreadCallback(callback));
        }

        public void GetFriendsListVisibility(bool forceReload,
            Action<FriendsListVisibilityStatus> callback)
        {
            using (var playersClient = getPlayersClient())
            using (
                var task = playersClient.Call<AndroidJavaObject>("getCurrentPlayer", forceReload))
            {
                AndroidTaskUtils.AddOnSuccessListener<AndroidJavaObject>(task, annotatedData =>
                {
                    AndroidJavaObject currentPlayerInfo =
                        annotatedData.Call<AndroidJavaObject>("get").Call<AndroidJavaObject>(
                            "getCurrentPlayerInfo");
                    int playerListVisibility =
                        currentPlayerInfo.Call<int>("getFriendsListVisibilityStatus");
                    InvokeCallbackOnGameThread(callback,
                        AndroidJavaConverter.ToFriendsListVisibilityStatus(playerListVisibility));
                });
                AndroidTaskUtils.AddOnFailureListener(task, exception =>
                {
                    InvokeCallbackOnGameThread(callback, FriendsListVisibilityStatus.NetworkError);
                    return;
                });
            }
        }

#pragma warning disable 0618 // Deprecated Unity APIs
        public IUserProfile[] GetFriends()
#pragma warning restore 0618
        {
            return mFriends;
        }

        public string GetUserId()
        {
            if (mUser == null)
            {
                return null;
            }

            return mUser.id;
        }

        public string GetUserDisplayName()
        {
            if (mUser == null)
            {
                return null;
            }

            return mUser.userName;
        }

        public string GetUserImageUrl()
        {
            if (mUser == null)
            {
                return null;
            }

            return mUser.AvatarURL;
        }

        public void GetPlayerStats(Action<CommonStatusCodes, PlayerStats> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            using (var client = getPlayerStatsClient())
            using (var task = client.Call<AndroidJavaObject>("loadPlayerStats", true))
            {
                AndroidTaskUtils.AddOnSuccessListener<AndroidJavaObject>(
                    task, annotatedData =>
                    {
                        using (var playerStats = annotatedData.Call<AndroidJavaObject>("get"))
                        {
                            PlayerStats stats;
                            if (playerStats != null)
                            {
                                // Fixed: Use constructor instead of assigning to read-only properties
                                stats = new PlayerStats(
                                    playerStats.Call<int>("getNumberOfPurchases"),
                                    playerStats.Call<float>("getAverageSessionLength"), // Fixed typo: AvgSessonLength -> AverageSessionLength
                                    playerStats.Call<int>("getDaysSinceLastPlayed"),
                                    playerStats.Call<int>("getNumberOfSessions"),
                                    playerStats.Call<float>("getSessionPercentile"),
                                    playerStats.Call<float>("getSpendPercentile"),
                                    playerStats.Call<float>("getSpendProbability"),
                                    playerStats.Call<float>("getChurnProbability"),
                                    playerStats.Call<float>("getHighSpenderProbability"),
                                    playerStats.Call<float>("getTotalSpendNext28Days")
                                );
                            }
                            else
                            {
                                stats = new PlayerStats(); // Creates an invalid stats object
                            }

                            callback(CommonStatusCodes.Success, stats);
                        }
                    });
                AndroidTaskUtils.AddOnFailureListener(task, exception =>
                {
                    PlayerStats stats = new PlayerStats(); // Creates an invalid stats object
                    callback(CommonStatusCodes.ApiNotConnected, stats);
                });
            }
        }

#pragma warning disable 0618 // Deprecated Unity APIs
        public void LoadUsers(string[] userIds, Action<IUserProfile[]> callback)
#pragma warning restore 0618
        {
            callback = AsOnGameThreadCallback(callback);
            using (var playersClient = getPlayersClient())
            using (var task = playersClient.Call<AndroidJavaObject>("loadPlayers", userIds))
            {
                AndroidTaskUtils.AddOnSuccessListener<AndroidJavaObject>(
                    task, annotatedData =>
                    {
                        using (var playersBuffer = annotatedData.Call<AndroidJavaObject>("get"))
                        {
                            int count = playersBuffer.Call<int>("getCount");
#pragma warning disable 0618 // Deprecated Unity APIs
                            IUserProfile[] users = new IUserProfile[count];
#pragma warning restore 0618
                            for (int i = 0; i < count; ++i)
                            {
                                using (var player = playersBuffer.Call<AndroidJavaObject>("get", i))
                                {
                                    users[i] = AndroidJavaConverter.ToPlayerProfile(player);
                                }
                            }

                            callback(users);
                        }
                    });
                AndroidTaskUtils.AddOnFailureListener(task, exception =>
                {
                    OurUtils.Logger.e("LoadUsers failed: " + exception.Call<string>("toString"));
#pragma warning disable 0618 // Deprecated Unity APIs
                    callback(new IUserProfile[0]);
#pragma warning restore 0618
                });
            }
        }

        public void LoadAchievements(Action<Achievement[]> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            using (var client = getAchievementsClient())
            using (var task = client.Call<AndroidJavaObject>("load", true))
            {
                AndroidTaskUtils.AddOnSuccessListener<AndroidJavaObject>(
                    task, annotatedData =>
                    {
                        using (var achievementBuffer = annotatedData.Call<AndroidJavaObject>("get"))
                        {
                            int count = achievementBuffer.Call<int>("getCount");
                            Achievement[] data = new Achievement[count];
                            for (int i = 0; i < count; ++i)
                            {
                                using (var ach = achievementBuffer.Call<AndroidJavaObject>("get", i))
                                {
                                    data[i] = new Achievement();
                                    data[i].Id = ach.Call<string>("getAchievementId");
                                    data[i].IsIncremental = ach.Call<int>("getType") == 1;
                                    data[i].IsUnlocked = ach.Call<int>("getState") == 0;
                                    data[i].IsRevealed = ach.Call<int>("getState") != 2;
                                    if (data[i].IsIncremental)
                                    {
                                        data[i].CurrentSteps = ach.Call<int>("getCurrentSteps");
                                        data[i].TotalSteps = ach.Call<int>("getTotalSteps");
                                    }

                                    data[i].Name = ach.Call<string>("getName");
                                    data[i].Description = ach.Call<string>("getDescription");
                                    data[i].Points = (ulong) ach.Call<long>("getXpValue");
                                    data[i].LastModifiedTime =
                                        AndroidJavaConverter.ToDateTime(ach.Call<long>("getLastUpdatedTimestamp"));
                                    if (ach.Call<AndroidJavaObject>("getRevealedImageUri") != null)
                                    {
                                        data[i].RevealedImageUrl =
                                            ach.Call<AndroidJavaObject>("getRevealedImageUri")
                                                .Call<string>("toString");
                                    }

                                    if (ach.Call<AndroidJavaObject>("getUnlockedImageUri") != null)
                                    {
                                        data[i].UnlockedImageUrl =
                                            ach.Call<AndroidJavaObject>("getUnlockedImageUri")
                                                .Call<string>("toString");
                                    }
                                }
                            }

                            callback(data);
                        }
                    });
                AndroidTaskUtils.AddOnFailureListener(task, exception =>
                {
                    OurUtils.Logger.e("LoadAchievements failed: " + exception.Call<string>("toString"));
                    callback(new Achievement[0]);
                });
            }
        }

        public void UnlockAchievement(string achId, Action<bool> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            using (var client = getAchievementsClient())
            {
                client.Call("unlock", achId);
            }

            // Unlock does not have a callback.
            callback(true);
        }

        public void RevealAchievement(string achId, Action<bool> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            using (var client = getAchievementsClient())
            {
                client.Call("reveal", achId);
            }

            // Reveal does not have a callback.
            callback(true);
        }

        public void IncrementAchievement(string achId, int steps, Action<bool> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            Misc.CheckNotNull(achId);
            using (var client = getAchievementsClient())
            {
                client.Call("increment", achId, steps);
            }

            // Increment does not have a callback.
            callback(true);
        }

        public void SetStepsAtLeast(string achId, int steps, Action<bool> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            Misc.CheckNotNull(achId);
            using (var client = getAchievementsClient())
            {
                client.Call("setSteps", achId, steps);
            }

            // SetSteps does not have a callback.
            callback(true);
        }

        public void ShowAchievementsUI(Action<UIStatus> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            using (var client = getAchievementsClient())
            using (var task = client.Call<AndroidJavaObject>("getAchievementsIntent"))
            {
                AndroidTaskUtils.AddOnSuccessListener<AndroidJavaObject>(
                    task, intent =>
                    {
                        AndroidHelperFragment.ShowAchievementsUI( // Fixed: Call correct helper method
                            AsOnGameThreadCallback(callback));
                    });
                AndroidTaskUtils.AddOnFailureListener(task, exception =>
                {
                    OurUtils.Logger.e("ShowAchievementsUI failed: " + exception.Call<string>("toString"));
                    callback(UIStatus.InternalError);
                });
            }
        }

        public int LeaderboardMaxResults()
        {
            return mLeaderboardMaxResults;
        }

#pragma warning disable 0618 // Deprecated Unity APIs
        public void ShowLeaderboardUI(string leaderboardId, LeaderboardTimeSpan span,
            Action<UIStatus> callback)
#pragma warning restore 0618
        {
            callback = AsOnGameThreadCallback(callback);
            using (var client = getLeaderboardsClient())
            {
                // Fixed: Call correct helper method
                if (leaderboardId == null)
                {
                    AndroidHelperFragment.ShowAllLeaderboardsUI(AsOnGameThreadCallback(callback));
                }
                else
                {
                    AndroidHelperFragment.ShowLeaderboardUI(leaderboardId, span, AsOnGameThreadCallback(callback));
                }
            }
        }

#pragma warning disable 0618 // Deprecated Unity APIs
        public void LoadScores(string leaderboardId, LeaderboardStart start, int rowCount,
            LeaderboardCollection collection, LeaderboardTimeSpan timeSpan,
            Action<LeaderboardScoreData> callback)
#pragma warning restore 0618
        {
            callback = AsOnGameThreadCallback(callback);
            using (var client = getLeaderboardsClient())
            {
                AndroidJavaObject task = null;
                if (start == LeaderboardStart.PlayerCentered)
                {
                    task = client.Call<AndroidJavaObject>("loadPlayerCenteredScores",
                        leaderboardId,
#pragma warning disable 0618 // Deprecated Unity APIs
                        AndroidJavaConverter.ToLeaderboardVariantTimeSpan(timeSpan),
                        AndroidJavaConverter.ToLeaderboardVariantCollection(collection),
#pragma warning restore 0618
                        rowCount,
                        true);
                }
                else
                {
                    task = client.Call<AndroidJavaObject>("loadTopScores",
                        leaderboardId,
#pragma warning disable 0618 // Deprecated Unity APIs
                        AndroidJavaConverter.ToLeaderboardVariantTimeSpan(timeSpan),
                        AndroidJavaConverter.ToLeaderboardVariantCollection(collection),
#pragma warning restore 0618
                        rowCount,
                        true);
                }

                using (task)
                {
                    AndroidTaskUtils.AddOnSuccessListener<AndroidJavaObject>(
                        task, annotatedData =>
                        {
                            using (var scores = annotatedData.Call<AndroidJavaObject>("get"))
                            {
                                callback(CreateLeaderboardScoreData(
                                    leaderboardId,
                                    start,
                                    rowCount,
                                    collection,
                                    timeSpan,
                                    ResponseStatus.Success,
                                    scores));
                            }
                        });
                    AndroidTaskUtils.AddOnFailureListener(task, exception =>
                    {
                        OurUtils.Logger.e("LoadScores failed: " + exception.Call<string>("toString"));
                        callback(new LeaderboardScoreData(leaderboardId, ResponseStatus.InternalError));
                    });
                }
            }
        }

        public void LoadMoreScores(ScorePageToken token, int rowCount,
            Action<LeaderboardScoreData> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            using (var client = getLeaderboardsClient())
            using (var task = client.Call<AndroidJavaObject>("loadMoreScores",
                token.InternalObject, rowCount,
                AndroidJavaConverter.ToPageDirection(token.Direction)))
            {
                AndroidTaskUtils.AddOnSuccessListener<AndroidJavaObject>(
                    task, annotatedData =>
                    {
                        using (var scores = annotatedData.Call<AndroidJavaObject>("get"))
                        {
                            // Fixed: Pass correct arguments to constructor
                            callback(CreateLeaderboardScoreData(
                                token.LeaderboardId,
                                LeaderboardStart.TopScores, // Start is not available in token, using default
                                rowCount,
                                token.Collection,
                                token.TimeSpan,
                                ResponseStatus.Success,
                                scores));
                        }
                    });
                AndroidTaskUtils.AddOnFailureListener(task, exception =>
                {
                    OurUtils.Logger.e("LoadMoreScores failed: " + exception.Call<string>("toString"));
                    callback(new LeaderboardScoreData(token.LeaderboardId, ResponseStatus.InternalError));
                });
            }
        }

#pragma warning disable 0618 // Deprecated Unity APIs
        private LeaderboardScoreData CreateLeaderboardScoreData(
            string leaderboardId,
            LeaderboardStart start,
            int rowCount,
            LeaderboardCollection collection,
            LeaderboardTimeSpan timeSpan,
            ResponseStatus status,
            AndroidJavaObject scores)
#pragma warning restore 0618
        {
            LeaderboardScoreData data = new LeaderboardScoreData(leaderboardId, status);
            if (scores == null)
            {
                return data;
            }

            using (var lb = scores.Call<AndroidJavaObject>("getLeaderboard"))
            {
                data.Title = lb.Call<string>("getDisplayName");
                data.ApproximateCount = (ulong) lb.Call<long>("getApproximatePlayerCount");
            }

            using (var scoreBuffer = scores.Call<AndroidJavaObject>("getScores"))
            {
                int count = scoreBuffer.Call<int>("getCount");
                for (int i = 0; i < count; ++i)
                {
                    using (var score = scoreBuffer.Call<AndroidJavaObject>("get", i))
                    {
                        data.AddScore(new PlayGamesScore(
                            AndroidJavaConverter.ToDateTime(score.Call<long>("getTimestampMillis")),
                            leaderboardId,
                            (ulong) score.Call<long>("getRank"),
                            score.Call<AndroidJavaObject>("getScoreHolder").Call<string>("getPlayerId"),
                            (ulong) score.Call<long>("getRawScore"),
                            score.Call<string>("getScoreTag")));
                    }
                }

                // Add the player score if it exists.
                using (var score = scores.Call<AndroidJavaObject>("getOwnerScore"))
                {
                    if (score != null)
                    {
                        data.PlayerScore = new PlayGamesScore(
                            AndroidJavaConverter.ToDateTime(score.Call<long>("getTimestampMillis")),
                            leaderboardId,
                            (ulong) score.Call<long>("getRank"),
                            score.Call<AndroidJavaObject>("getScoreHolder").Call<string>("getPlayerId"),
                            (ulong) score.Call<long>("getRawScore"),
                            score.Call<string>("getScoreTag"));
                    }
                }

                // Add the page tokens
                using (var prevToken = scores.Call<AndroidJavaObject>("getPrevPageNextToken"))
                {
                    // Fixed: Pass correct arguments to constructor (excluding start)
                    data.PrevPageToken = new ScorePageToken(prevToken, leaderboardId, collection, timeSpan,
                        ScorePageDirection.Backward);
                }

                using (var nextToken = scores.Call<AndroidJavaObject>("getNextPageNextToken"))
                {
                    // Fixed: Pass correct arguments to constructor (excluding start)
                    data.NextPageToken = new ScorePageToken(nextToken, leaderboardId, collection, timeSpan,
                        ScorePageDirection.Forward);
                }
            }

            return data;
        }

        public void SubmitScore(string leaderboardId, long score, Action<bool> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            using (var client = getLeaderboardsClient())
            {
                client.Call("submitScore", leaderboardId, score);
            }

            // SubmitScore does not have a callback.
            callback(true);
        }

        public void SubmitScore(string leaderboardId, long score, string metadata,
            Action<bool> callback)
        {
            callback = AsOnGameThreadCallback(callback);
            using (var client = getLeaderboardsClient())
            {
                client.Call("submitScore", leaderboardId, score, metadata);
            }

            // SubmitScore does not have a callback.
            callback(true);
        }

        public ISavedGameClient GetSavedGameClient()
        {
            lock (GameServicesLock)
            {
                return mSavedGameClient;
            }
        }

        public IEventsClient GetEventsClient()
        {
            lock (GameServicesLock)
            {
                return mEventsClient;
            }
        }

        private AndroidJavaObject getAchievementsClient()
        {
            return mGamesClass.CallStatic<AndroidJavaObject>("getAchievementsClient",
                AndroidHelperFragment.GetActivity());
        }

        private AndroidJavaObject getPlayersClient()
        {
            return mGamesClass.CallStatic<AndroidJavaObject>("getPlayersClient",
                AndroidHelperFragment.GetActivity());
        }

        private AndroidJavaObject getLeaderboardsClient()
        {
            return mGamesClass.CallStatic<AndroidJavaObject>("getLeaderboardsClient",
                AndroidHelperFragment.GetActivity());
        }

        private AndroidJavaObject getPlayerStatsClient()
        {
            return mGamesClass.CallStatic<AndroidJavaObject>("getPlayerStatsClient",
                AndroidHelperFragment.GetActivity());
        }

        private AndroidJavaObject getGamesSignInClient()
        {
            return mGamesClass.CallStatic<AndroidJavaObject>("getGamesSignInClient",
                AndroidHelperFragment.GetActivity());
        }

        private AndroidJavaObject getRecallClient()
        {
            return mGamesClass.CallStatic<AndroidJavaObject>("getRecallClient",
                AndroidHelperFragment.GetActivity());
        }

        // private AndroidJavaObject getAccount()
        // {
        //     // This method caused NoSuchMethodError because the underlying Java method
        //     // getAccount(Activity) doesn't exist in the current .aar library.
        //     // It's no longer needed as the client getters now use the simpler signature.
        //     // return mGamesClass.CallStatic<AndroidJavaObject>("getAccount",
        //     //     AndroidHelperFragment.GetActivity());
        // }
    }
}

#endif //UNITY_ANDROID