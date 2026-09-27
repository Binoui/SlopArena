#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using SlopArena.Client.UI;
using UnityEngine;

namespace SlopArena.Client.Network
{
    /// <summary>One launch-scoped Steam Room request at a time; Master is the sole admission authority.</summary>
    public sealed class SteamRoomJoinCoordinator
    {
        private const float DuplicateWindowSeconds = 5f;
        private readonly ChatSession _session;
        private readonly SteamRoomJoinPrompt _prompt = new();
        private Guid _target;
        private int _generation;
        private bool _busy;
        private TaskCompletionSource<bool>? _decision;
        private Guid _lastFinishedTarget;
        private float _lastFinishedAt;

        public bool HasPendingJoin => _target != Guid.Empty;
        public Guid PendingRoomId => _target;
        public static bool CanBegin(bool needsDisplayName, bool busy, Guid target) =>
            !needsDisplayName && !busy && target != Guid.Empty;
        public static bool IsDuplicate(Guid previous, float finishedAt, Guid next, float now) =>
            next != Guid.Empty && previous == next &&
            now >= finishedAt && now - finishedAt < DuplicateWindowSeconds;

        public SteamRoomJoinCoordinator(ChatSession session) => _session = session;

        public void Receive(string? payload)
        {
            if (string.IsNullOrEmpty(payload)) return; // Ordinary Steam launch.
            if (_target != Guid.Empty || _busy) return; // No competing intent can replace the current one.
            if (!RoomConnection.TryParse(payload, out var roomId))
            {
                _prompt.Notice("Invalid Steam Room request. Ask your friend to send Join Game again.",
                    !FrontendController.IsFrontendActive);
                return;
            }
            if (IsDuplicate(_lastFinishedTarget, _lastFinishedAt, roomId, Time.unscaledTime))
                return;
            _prompt.Clear();
            _target = roomId;
        }
        public void RejectInvalid()
        {
            if (_target == Guid.Empty && !_busy)
                _prompt.Notice("Invalid Steam Room request. Ask your friend to send Join Game again.",
                    !FrontendController.IsFrontendActive);
        }


        public void Tick()
        {
            _prompt.Tick();
            if (_decision != null && !_decision.Task.IsCompleted && (!_prompt.IsPresented ||
                !FrontendController.IsFrontendActive && MatchConfig.Mode == GameMode.PvP))
            {
                Cancel();
                _prompt.Notice("The Room changed while you were deciding. Finish or leave first, then request Join Game again.",
                    !FrontendController.IsFrontendActive);
            }
            if (!CanBegin(_session.NeedsDisplayName, _busy, _target)) return;
            _busy = true;
            _ = ProcessAsync(_target, _generation);
        }

        public void Cancel()
        {
            _generation++;
            _target = Guid.Empty;
            _lastFinishedTarget = Guid.Empty;
            _decision?.TrySetResult(false);
            _decision = null;
            _prompt.Clear();
        }

        private bool Current(int generation, LobbyClient? lobby = null, long steamId = 0) =>
            generation == _generation && _target != Guid.Empty &&
            (lobby == null || ReferenceEquals(_session.ActiveLobby, lobby) &&
                _session.IsConnected && _session.MasterClient?.IsSteamAuthenticated == true &&
                _session.MasterClient.TokenExpiresAt > DateTimeOffset.UtcNow &&
                _session.SteamId == steamId);

        private async Task ProcessAsync(Guid target, int generation)
        {
            try
            {
                if (ClientSession.SelectedOnlineMode == ClientSession.OnlineSelection.Room &&
                    ClientSession.SelectedRoomId == target)
                {
                    // Never pull a player out of a fight, selection or Results for the same Room.
                    if (FrontendController.IsFrontendActive &&
                        FrontendController.CurrentPage == FrontendPage.ServerBrowser)
                        FrontendController.Show(FrontendPage.LobbyRoom);
                    return;
                }
                if (!FrontendController.IsFrontendActive && MatchConfig.Mode == GameMode.PvP)
                {
                    _prompt.Notice("Finish or leave this match, then request Join Game again.", true);
                    return;
                }
                if (ClientSession.SelectedServerId != Guid.Empty)
                {
                    _prompt.Notice("Leave the current server before joining a Steam Room.",
                        !FrontendController.IsFrontendActive);
                    return;
                }
                if (!await _session.EnsureConnectedAsync() || !Current(generation))
                {
                    if (Current(generation))
                        _prompt.Notice("Steam or the Room directory is unavailable. Reconnect, then request Join Game again.",
                            !FrontendController.IsFrontendActive);
                    return;
                }
                var lobby = _session.ActiveLobby;
                long steamId = _session.SteamId ?? 0;
                if (lobby == null || steamId == 0) return;
                if (_session.MasterClient?.IsSteamAuthenticated != true ||
                    _session.MasterClient.TokenExpiresAt <= DateTimeOffset.UtcNow)
                {
                    _prompt.Notice("Your Master session has expired. Reconnect with Steam, then request Join Game again.",
                        !FrontendController.IsFrontendActive);
                    return;
                }

                var current = await lobby.GetMyRoomAsync();
                if (!Current(generation, lobby, steamId)) return;
                Guid initialRoomId = current?.Id ?? Guid.Empty;
                if (current?.Id == target)
                {
                    // A prior browser join or a duplicate request already admitted us.
                    if (FrontendController.IsFrontendActive &&
                        FrontendController.CurrentPage == FrontendPage.ServerBrowser)
                    {
                        ServerBrowserUI.AdoptRoom(_session, current);
                        FrontendController.Show(FrontendPage.LobbyRoom);
                    }
                    return;
                }
                if (current != null && !Switchable(current))
                {
                    _prompt.Notice("Your Room is starting or playing a match. Finish or leave first, then request Join Game again.",
                        !FrontendController.IsFrontendActive);
                    return;
                }
                if (!await DestinationAvailable(lobby, target) || !Current(generation, lobby, steamId))
                {
                    if (Current(generation, lobby, steamId))
                        _prompt.Notice("That Room is full, unavailable or has closed. Ask your friend to invite you again.",
                            !FrontendController.IsFrontendActive);
                    return;
                }

                current = await lobby.GetMyRoomAsync();
                if (!Current(generation, lobby, steamId)) return;
                if (!CanSwitchAfterConfirmation(initialRoomId, current))
                {
                    _prompt.Notice("Your current Room changed. Finish or leave first, then request Join Game again.",
                        !FrontendController.IsFrontendActive);
                    return;
                }
                if (current != null || !FrontendController.IsFrontendActive)
                {
                    _decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    string message = current != null
                        ? "Leave current server and join this one?"
                        : "End local play and join your friend's Room?";
                    if (!_prompt.TryConfirm("JOIN STEAM ROOM", message,
                        () => _decision?.TrySetResult(true),
                        () => _decision?.TrySetResult(false), !FrontendController.IsFrontendActive))
                    {
                        _prompt.Notice("Close the current dialog, then request Join Game again.",
                            !FrontendController.IsFrontendActive);
                        return;
                    }
                    bool accepted = await _decision.Task;
                    _decision = null;
                    _prompt.Clear();
                    if (!Current(generation, lobby, steamId)) return;
                    if (!accepted)
                    {
                        _prompt.Notice("Join cancelled. Your current Room and local play are unchanged.",
                            !FrontendController.IsFrontendActive);
                        return;
                    }
                }

                // Revalidate after any user decision; a selection can have advanced to a match.
                current = await lobby.GetMyRoomAsync();
                if (!Current(generation, lobby, steamId)) return;
                if (current?.Id == target)
                {
                    ServerBrowserUI.AdoptRoom(_session, current);
                    FrontendController.Show(FrontendPage.LobbyRoom);
                    return;
                }
                if (!CanSwitchAfterConfirmation(initialRoomId, current) ||
                    !FrontendController.IsFrontendActive && MatchConfig.Mode == GameMode.PvP)
                {
                    _prompt.Notice("Your Room changed or started a match. Nothing was left or joined.",
                        !FrontendController.IsFrontendActive);
                    return;
                }
                if (!await DestinationAvailable(lobby, target) || !Current(generation, lobby, steamId))
                {
                    if (Current(generation, lobby, steamId))
                        _prompt.Notice("The destination is no longer joinable. Your current Room was not left.", false);
                    return;
                }
                current = await lobby.GetMyRoomAsync();
                if (!Current(generation, lobby, steamId)) return;
                if (!CanSwitchAfterConfirmation(initialRoomId, current))
                {
                    _prompt.Notice("Your Room started a match or changed before leave. Nothing was switched.",
                        !FrontendController.IsFrontendActive);
                    return;
                }
                if (current == null && !FrontendController.IsFrontendActive)
                    FrontendController.Show(FrontendPage.Home); // Approval ends local play before admission.
                if (current != null)
                {
                    try
                    {
                        await lobby.LeaveRoomAsync();
                    }
                    catch (Exception)
                    {
                        if (Current(generation, lobby, steamId))
                            await ResolveUncertainLeave(lobby, generation, steamId);
                        return;
                    }
                    if (!Current(generation, lobby, steamId)) return;
                    if (lobby.JoinedRoomId != Guid.Empty)
                    {
                        await ResolveUncertainLeave(lobby, generation, steamId);
                        return;
                    }
                    ClientSession.SelectedRoomId = Guid.Empty;
                    ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.LegacyServer;
                    ClientSession.SelectedServerName = string.Empty;
                }

                RoomMembershipResult joined;
                try { joined = await lobby.JoinRoomAsync(target); }
                catch (Exception ex)
                {
                    if (Current(generation, lobby, steamId))
                    {
                        ServerBrowserUI.PendingReturnNotice = DescribeJoinFailure(ex);
                        FrontendController.Show(FrontendPage.ServerBrowser);
                    }
                    return;
                }
                if (!Current(generation, lobby, steamId))
                {
                    try { await lobby.LeaveRoomIfCurrentAsync(joined.Room.Id,
                        joined.OperationGeneration, joined.MembershipGeneration); }
                    catch (Exception ex) { Debug.LogWarning($"[SteamRoomJoin] Stale join cleanup failed: {ex.Message}"); }
                    return;
                }
                if (joined.Room.Id != target || lobby.JoinedRoomId != target)
                {
                    _prompt.Notice("Room admission did not match the requested Room. Return to the browser and retry.", false);
                    return;
                }
                ServerBrowserUI.AdoptRoom(_session, joined.Room);
                FrontendController.Show(FrontendPage.LobbyRoom);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SteamRoomJoin] Join failed: {ex.Message}");
                if (Current(generation))
                    _prompt.Notice("Couldn't reach the Room directory. Check your connection and request Join Game again.",
                        !FrontendController.IsFrontendActive);
            }
            finally
            {
                if (generation == _generation)
                {
                    _target = Guid.Empty;
                    _lastFinishedTarget = target;
                    _lastFinishedAt = Time.unscaledTime;
                }
                _busy = false;
            }
        }

        public static bool Switchable(RoomSnapshot room) =>
            room.Phase == "Lobby" || room.Phase == "Character Select" || room.Phase == "Stage Select";
        public static bool CanSwitchAfterConfirmation(Guid originalRoomId, RoomSnapshot? current) =>
            (current?.Id ?? Guid.Empty) == originalRoomId &&
            (current == null || Switchable(current));

        public static bool DestinationJoinable(RoomSummary[]? rooms, Guid target) =>
            target != Guid.Empty && rooms != null &&
            rooms.Any(room => room.Id == target && room.Joinable);

        private static async Task<bool> DestinationAvailable(LobbyClient lobby, Guid target) =>
            DestinationJoinable(await lobby.GetRoomsAsync(), target);

        private async Task ResolveUncertainLeave(LobbyClient lobby, int generation, long steamId)
        {
            try
            {
                var actual = await lobby.GetMyRoomAsync();
                if (!Current(generation, lobby, steamId)) return;
                if (actual == null)
                {
                    ClientSession.SelectedRoomId = Guid.Empty;
                    ClientSession.SelectedOnlineMode = ClientSession.OnlineSelection.LegacyServer;
                    ClientSession.SelectedServerName = string.Empty;
                    ServerBrowserUI.PendingReturnNotice = "The Room leave completed, but the new Room was not joined. Choose a Room again.";
                    FrontendController.Show(FrontendPage.ServerBrowser);
                }
                else
                {
                    ServerBrowserUI.AdoptRoom(_session, actual);
                    _prompt.Notice("The Room leave was not confirmed. You are still in your Room; request Join Game again after reconnecting.", false);
                }
            }
            catch (Exception)
            {
                if (Current(generation))
                    _prompt.Notice("Room membership is uncertain. Reconnect to resynchronize before requesting Join Game again.", false);
            }
        }

        private static string DescribeJoinFailure(Exception ex)
        {
            string message = ex.Message;
            if (message.Contains("room_full", StringComparison.OrdinalIgnoreCase))
                return "That Room filled before you joined. Choose another Room or ask for a new invite.";
            if (message.Contains("room_not_found", StringComparison.OrdinalIgnoreCase))
                return "That Room has closed. Choose another Room or ask for a new invite.";
            if (message.Contains("room_selecting", StringComparison.OrdinalIgnoreCase))
                return "That Room has started selecting or playing. Ask your friend to invite you when it returns to Lobby.";
            return "The Master did not admit you to that Room. Check your Steam access and retry from the browser.";
        }
    }
}
