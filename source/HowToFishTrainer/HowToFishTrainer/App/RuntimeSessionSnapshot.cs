using HowToFishTrainer.Core;

namespace HowToFishTrainer.App;

public sealed record RuntimeSessionSnapshot(bool Host, bool Client, bool Lobby, string? LobbyId, int? OwnerId, ulong SceneHandle)
{
    internal SessionMode Mode => RuntimeSessionMode.From(Host, Client, Lobby);

    internal bool RequiresClearFrom(RuntimeSessionSnapshot prior, bool reconnecting)
    {
        if (Mode != prior.Mode) return true;
        if (Mode == SessionMode.MainMenu) return false;
        if (Mode is SessionMode.Host or SessionMode.JoinedFriend)
        {
            if (reconnecting && (string.IsNullOrEmpty(prior.LobbyId) || string.IsNullOrEmpty(LobbyId))) return true;
            if (!string.IsNullOrEmpty(prior.LobbyId) && !string.IsNullOrEmpty(LobbyId) && prior.LobbyId != LobbyId) return true;
            if (prior.OwnerId.HasValue && (reconnecting || OwnerId.HasValue) && prior.OwnerId != OwnerId) return true;
        }
        if (!reconnecting) return false;
        if (prior.SceneHandle != 0) return SceneHandle != prior.SceneHandle;
        return string.IsNullOrEmpty(prior.LobbyId) || prior.LobbyId != LobbyId;
    }

    internal RuntimeSessionSnapshot KeepKnownIdentity(RuntimeSessionSnapshot prior)
    {
        if ((!string.IsNullOrEmpty(LobbyId) || string.IsNullOrEmpty(prior.LobbyId)) &&
            (OwnerId.HasValue || !prior.OwnerId.HasValue) && (SceneHandle != 0 || prior.SceneHandle == 0)) return this;
        return this with { LobbyId = string.IsNullOrEmpty(LobbyId) ? prior.LobbyId : LobbyId,
                           OwnerId = OwnerId ?? prior.OwnerId, SceneHandle = SceneHandle == 0 ? prior.SceneHandle : SceneHandle };
    }
}
