using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Exiled.API.Enums;
using Exiled.API.Features;
using Exiled.API.Features.Items;
using Exiled.API.Features.Pickups;
using Exiled.Events.EventArgs.Player;
using MEC;
using PlayerRoles;
using UnityEngine;
using Utils.NonAllocLINQ;

namespace PointTakeOver;

public class EventHandlers
{
    private readonly Dictionary<ZoneType, RoomType[]> _takeoverPointSpawns = new()
    {
        {ZoneType.LightContainment, [RoomType.LczCrossing, RoomType.LczCurve, RoomType.LczStraight, RoomType.LczTCross, RoomType.LczPlants, RoomType.LczToilets]},
        {ZoneType.HeavyContainment, [RoomType.HczCrossing, RoomType.HczArmory, RoomType.HczCurve, RoomType.HczHid, RoomType.HczStraight, RoomType.HczTCross] }
    };
    private readonly Dictionary<ZoneType, RoomType[]> _playerSpawnRooms = new()
    {
        { ZoneType.LightContainment, [RoomType.LczAirlock, RoomType.LczCafe, RoomType.LczCrossing, RoomType.LczCurve, RoomType.LczPlants, RoomType.LczStraight, RoomType.LczToilets, RoomType.LczTCross]},
        { ZoneType.HeavyContainment, [RoomType.HczCrossing, RoomType.HczCurve, RoomType.HczHid, RoomType.HczStraight, RoomType.HczTCross ]},
        { ZoneType.Entrance, [RoomType.EzConference, RoomType.EzCafeteria, RoomType.EzCurve, RoomType.EzStraight, RoomType.EzTCross, RoomType.EzCrossing]}
    };

    private readonly Dictionary<string, HashSet<Player>> _pointAPlayers = new() {["A"] = [], ["B"] = []};
    private readonly Dictionary<string, HashSet<Player>> _pointBPlayers = new() {["A"] = [], ["B"] = []};

    private HashSet<Player> _teamA = [];
    private HashSet<Player> _teamB = [];

    private readonly HashSet<Room> _rooms = [];
    private bool _runGame = true;
    private Room _takeoverPointA;
    private Room _takeoverPointB;
    private Vector3 _takeoverPointAPos;
    private Vector3 _takeoverPointBPos;
    private int _gameTime;
    private int _pointAOccupyTime;
    private int _pointBOccupyTime;

    private readonly List<CoroutineHandle> _coroutines = new();

    private Color MixColors(Color color1, Color color2, float ratio)
    {
        if (ratio is < 0 or > 1)
        {
            throw new System.ArgumentException("Ratio must be between 0 and 1 inclusive.");
        }

        var mixedRed = Mathf.Lerp(color1.r, color2.r, ratio);
        var mixedGreen = Mathf.Lerp(color1.g, color2.g, ratio);
        var mixedBlue = Mathf.Lerp(color1.b, color2.b, ratio);
        var mixedAlpha = Mathf.Lerp(color1.a, color2.a, ratio);

        return new Color(mixedRed, mixedGreen, mixedBlue, mixedAlpha);
    }

    private string MakeGradientText(string text, Color colorA, Color colorB)
    {
        var gradientText = "";
        for (var i = 0; i < text.Length; i++)
        {
            var ratio = i / (float) text.Length;
            var color = MixColors(colorA, colorB, ratio);
            gradientText += $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text[i]}</color>";
        }

        return gradientText;
    }

    public void OnMapGenerated()
    {
        var zoneType = PointTakeOver.Instance.Config.ZoneType;

        if (!_takeoverPointSpawns.TryGetValue(zoneType, out var rooms))
        {
            Log.Error($"Invalid zone type: {zoneType}");
            _runGame = false;
            return;
        }

        foreach (var room in Room.List.Where(x => x.Zone == zoneType && rooms.Contains(x.Type)))
        {
            _rooms.Add(room);
        }

        SetTakeoverPoint();

        Log.Debug($"포인트 A: {_takeoverPointA.Name} / 포인트 B: {_takeoverPointB.Name}");

        _takeoverPointA.Color = new Color(.96f, .25f, .48f, .1f);
        _takeoverPointB.Color = new Color(.25f, .61f, .96f, .1f);
    }

    private void SetTakeoverPoint()
    {
        var roomPositions = _rooms.Select(x => x.Position).ToList();
        var furthestRooms = GetFurthestRooms(roomPositions);

        _takeoverPointA = furthestRooms.roomA;
        _takeoverPointB = furthestRooms.roomB;

        _takeoverPointAPos = _takeoverPointA.Position;
        _takeoverPointBPos = _takeoverPointB.Position;
    }

    private (Room roomA, Room roomB) GetFurthestRooms(IReadOnlyList<Vector3> positions)
    {
        var maxDistance = 0f;
        Room roomA = null;
        Room roomB = null;

        for (var i = 0; i < positions.Count; i++)
        {
            for (var j = i + 1; j < positions.Count; j++)
            {
                var distance = Vector3.Distance(positions[i], positions[j]);
                if (!(distance > maxDistance)) continue;
                maxDistance = distance;
                roomA = _rooms.ElementAt(i);
                roomB = _rooms.ElementAt(j);
            }
        }

        return (roomA, roomB);
    }

    public void OnRoundStarted()
    {
        if (!_runGame)
            return;

        _coroutines.Add(Timing.RunCoroutine(StartGame()));
    }

    private IEnumerator<float> StartGame()
    {
        yield return Timing.WaitForSeconds(0.1f);
        Round.IsLocked = true;
        
        foreach (var player in Player.List)
        {
            player.Role.Set(RoleTypeId.Spectator, SpawnReason.None, RoleSpawnFlags.All);
            player.Broadcast(10, "<size=35><b>MTF-E11-SR 부착물을 설정해 주세요.</b></size>");
        }

        yield return Timing.WaitForSeconds(30f);

        Pickup.List.ToList().ForEach(x => x.Destroy());

        var players = Player.List.ToList();
        _teamA = players.Take(players.Count / 2).ToHashSet();
        _teamB = players.Skip(players.Count / 2).ToHashSet();

        foreach (var teamAPlayer in _teamA)
        {
            teamAPlayer.Broadcast(6, $"<size=35><b>당신은 {MakeGradientText("Class-D", new Color32(239,121,4, 255), new Color32(85,38,0,255))} 팀 입니다.</b></size>");
        }

        foreach (var teamBPlayer in _teamB)
        {
            teamBPlayer.Broadcast(6, $"<size=35><b>당신은 {MakeGradientText("Nine-Tailed-Fox", new Color32(7,143,243,255), new Color32(0,46,85,255))} 팀 입니다.</b></size>");
        }

        yield return Timing.WaitForSeconds(5f);

        Map.Broadcast(3, "<size=35><b>포인트 A와 B를 점령하세요!</b></size>", Broadcast.BroadcastFlags.Normal, true);

        yield return Timing.WaitForSeconds(3f);

        var spawnableRooms = Room.List.Where(x =>
            _playerSpawnRooms[PointTakeOver.Instance.Config.ZoneType].Contains(x.Type) && x != _takeoverPointA &&
            x != _takeoverPointB).ToList();

        foreach (var aPlayer in _teamA)
        {
            aPlayer.Role.Set(RoleTypeId.ClassD, SpawnReason.Respawn, RoleSpawnFlags.None);
            spawnableRooms.ShuffleList();
            aPlayer.Position = spawnableRooms.First().Position + new Vector3(0, 1, 0);
            aPlayer.Broadcast(3, "<size=35><b>시작!</b></size>", Broadcast.BroadcastFlags.Normal, true);
        }

        foreach (var bPlayer in _teamB)
        {
            bPlayer.Role.Set(RoleTypeId.NtfSergeant, SpawnReason.Respawn, RoleSpawnFlags.None);
            spawnableRooms.ShuffleList();
            bPlayer.Position = spawnableRooms.First().Position + new Vector3(0, 1, 0);
            bPlayer.Broadcast(3, "<size=35><b>시작!</b></size>", Broadcast.BroadcastFlags.Normal, true);
        }

        foreach (var player in Player.List)
        {
            player.AddItem(ItemType.GunE11SR);
            player.AddItem(ItemType.Medkit);
            player.AddItem(ItemType.Adrenaline);
            player.AddItem(ItemType.ArmorCombat);
        }

        _coroutines.Add(Timing.RunCoroutine(PointTimer()));
    }

    private IEnumerator<float> PointTimer()
    {
        while (!Round.IsEnded)
        {
            foreach (var player in Player.List)
            {
                var distanceToA = Vector3.Distance(player.Position, _takeoverPointAPos);
                var distanceToB = Vector3.Distance(player.Position, _takeoverPointBPos);

                Log.Debug($"{player.Nickname} 체크 중, A: {distanceToA}m, B: {distanceToB}m");

                if (distanceToA < PointTakeOver.Instance.Config.CalculateDistance &&
                    distanceToB > PointTakeOver.Instance.Config.CalculateDistance)
                {
                    Log.Debug($"{player.Nickname} 포인트 A 근처에 있음.");
                    if (player.CurrentRoom == _takeoverPointA)
                    {
                        if (_teamA.Contains(player))
                        {
                            _pointAPlayers["A"].Add(player);
                            Log.Debug($"{player.Nickname} (A) 포인트 A에 있음. {_pointAPlayers.Count}");
                        } else if (_teamB.Contains(player))
                        {
                            _pointAPlayers["B"].Add(player);
                            Log.Debug($"{player.Nickname} (B) 포인트 A에 있음. {_pointAPlayers.Count}");
                        }
                    }
                    else
                    {
                        _pointAPlayers["A"].Remove(player);
                        _pointAPlayers["B"].Remove(player);
                    }

                } else if (distanceToA > PointTakeOver.Instance.Config.CalculateDistance &&
                           distanceToB < PointTakeOver.Instance.Config.CalculateDistance)
                {
                    Log.Debug($"{player.Nickname} 포인트 B 근처에 있음.");
                    if (player.CurrentRoom == _takeoverPointB)
                    {
                        if (_teamA.Contains(player))
                        {
                            _pointBPlayers["A"].Add(player);
                            Log.Debug($"{player.Nickname} (A) 포인트 B에 있음. {_pointBPlayers.Count}");
                        } else if (_teamB.Contains(player))
                        {
                            _pointBPlayers["B"].Add(player);
                            Log.Debug($"{player.Nickname} (B) 포인트 B에 있음. {_pointBPlayers.Count}");
                        }
                    }
                    else
                    {
                        _pointBPlayers["A"].Remove(player);
                        _pointBPlayers["B"].Remove(player);
                    }
                } else if (distanceToA < PointTakeOver.Instance.Config.CalculateDistance &&
                           distanceToB < PointTakeOver.Instance.Config.CalculateDistance)
                {
                    Log.Debug($"{player.Nickname} 두 포인트 모두 근처에 있음.");
                    if (player.CurrentRoom == _takeoverPointA)
                    {
                        if (_teamA.Contains(player))
                        {
                            _pointAPlayers["A"].Add(player);
                            Log.Debug($"{player.Nickname} (A) 포인트 A에 있음. {_pointAPlayers.Count}");
                        } else if (_teamB.Contains(player))
                        {
                            _pointAPlayers["B"].Add(player);
                            Log.Debug($"{player.Nickname} (B) 포인트 A에 있음. {_pointAPlayers.Count}");
                        }
                    }
                    else if (player.CurrentRoom == _takeoverPointB)
                    {
                        if (_teamA.Contains(player))
                        {
                            _pointBPlayers["A"].Add(player);
                            Log.Debug($"{player.Nickname} (A) 포인트 B에 있음. {_pointBPlayers.Count}");
                        } else if (_teamB.Contains(player))
                        {
                            _pointBPlayers["B"].Add(player);
                            Log.Debug($"{player.Nickname} (B) 포인트 B에 있음. {_pointBPlayers.Count}");
                        }
                    }
                    else
                    {
                        _pointAPlayers["A"].Remove(player);
                        _pointAPlayers["B"].Remove(player);
                        _pointBPlayers["A"].Remove(player);
                        _pointBPlayers["B"].Remove(player);
                    }
                }
                else
                {
                    _pointAPlayers["A"].Remove(player);
                    _pointAPlayers["B"].Remove(player);
                    _pointBPlayers["A"].Remove(player);
                    _pointBPlayers["B"].Remove(player);
                    Log.Debug($"{player.Nickname} 두 포인트 모두 근처에 없음.");
                }
            }

            // point A
            if (_pointAPlayers["A"].Count > 0)
            {
                if (_pointAPlayers["B"].Count == 0)
                {
                    Log.Debug("TeamA가 포인트 A 점령 중...");
                    foreach (var player in _pointAPlayers["A"])
                    {
                        player.ShowHint("포인트 A를 점령 중...", 2f);
                        _pointAOccupyTime++;
                    }

                    if (_pointAOccupyTime >= PointTakeOver.Instance.Config.OccupyTime)
                    {
                        Map.Broadcast(5, "<size=35><b>포인트 A가 Class-D 팀에 의해 점령되었습니다!</b></size>", Broadcast.BroadcastFlags.Normal, true);
                    }
                }
                else
                {
                    Log.Debug("포인트 A에서 대치 중...");
                    _pointAOccupyTime = 0;
                    foreach (var player in _pointAPlayers["A"])
                    {
                        player.ShowHint("대치 중!", 2f);
                    }

                    foreach (var player in _pointAPlayers["B"])
                    {
                        player.ShowHint("대치 중!", 2f);
                    }
                }
            }
            else if (_pointAPlayers["B"].Count > 0)
            {
                if (_pointAPlayers["A"].Count == 0)
                {
                    Log.Debug("TeamB가 포인트 A 점령 중...");
                    foreach (var player in _pointAPlayers["B"])
                    {
                        player.ShowHint("포인트 A를 점령 중...", 2f);
                        _pointAOccupyTime--;
                    }

                    if (_pointAOccupyTime <= -PointTakeOver.Instance.Config.OccupyTime)
                    {
                        Map.Broadcast(5, "<size=35><b>포인트 A가 NTF 팀에 의해 점령되었습니다!</b></size>", Broadcast.BroadcastFlags.Normal, true);
                    }
                }
                else
                {
                    Log.Debug("포인트 A에서 대치 중...");
                    _pointAOccupyTime = 0;
                    foreach (var player in _pointAPlayers["A"])
                    {
                        player.ShowHint("대치 중!", 2f);
                    }

                    foreach (var player in _pointAPlayers["B"])
                    {
                        player.ShowHint("대치 중!", 2f);
                    }
                }
            }

            // point B
            if (_pointBPlayers["A"].Count > 0)
            {
                if (_pointBPlayers["B"].Count == 0)
                {
                    Log.Debug("TeamA가 포인트 B 점령 중...");
                    foreach (var player in _pointBPlayers["A"])
                    {
                        player.ShowHint("포인트 B를 점령 중...", 2f);
                        _pointBOccupyTime++;
                    }

                    if (_pointBOccupyTime >= PointTakeOver.Instance.Config.OccupyTime)
                    {
                        Map.Broadcast(5, "<size=35><b>포인트 B가 Class-D 팀에 의해 점령되었습니다!</b></size>", Broadcast.BroadcastFlags.Normal, true);
                    }
                }
                else
                {
                    Log.Debug("포인트 B에서 대치 중...");
                    _pointBOccupyTime = 0;
                    foreach (var player in _pointBPlayers["A"])
                    {
                        player.ShowHint("대치 중!", 2f);
                    }

                    foreach (var player in _pointBPlayers["B"])
                    {
                        player.ShowHint("대치 중!", 2f);
                    }
                }
            }
            else if (_pointBPlayers["B"].Count > 0)
            {
                if (_pointBPlayers["A"].Count == 0)
                {
                    Log.Debug("TeamB가 포인트 B 점령 중...");
                    foreach (var player in _pointBPlayers["B"])
                    {
                        player.ShowHint("포인트 B를 점령 중...", 2f);
                        _pointBOccupyTime--;
                    }

                    if (_pointBOccupyTime <= -PointTakeOver.Instance.Config.OccupyTime)
                    {
                        Map.Broadcast(5, "<size=35><b>포인트 B가 NTF 팀에 의해 점령되었습니다!</b></size>", Broadcast.BroadcastFlags.Normal, true);
                    }
                }
                else
                {
                    Log.Debug("포인트 B에서 대치 중...");
                    _pointBOccupyTime = 0;
                    foreach (var player in _pointBPlayers["A"])
                    {
                        player.ShowHint("대치 중!", 2f);
                    }

                    foreach (var player in _pointBPlayers["B"])
                    {
                        player.ShowHint("대치 중!", 2f);
                    }
                }
            }

            yield return Timing.WaitForSeconds(1f);
        }
    }

    public void OnDying(DyingEventArgs ev)
    {
        Timing.CallDelayed(3f, () =>
        {
            if (Ragdoll.List.Count(x => x.Owner == ev.Player) != 0)
            {
                Ragdoll.List.Where(x => x.Owner == ev.Player).ToList().ForEach(ragdoll =>
                {
                    ragdoll.Destroy();
                });
            }
        });
    }

    private IEnumerator<float> Respawn(Player player, ushort time)
    {
        var timeLeft = time;
        while (timeLeft > 0)
        {
            timeLeft--;
            player.Broadcast(2, $"<b><size=35>{timeLeft}뒤 리스폰됩니다.</size></b>", Broadcast.BroadcastFlags.Normal, true);
            yield return Timing.WaitForSeconds(1f);
        }

        player.Broadcast(3, "<size=35><b>리스폰되었습니다.</b></size>", Broadcast.BroadcastFlags.Normal, true);
        player.Role.Set(RoleTypeId.ClassD, SpawnReason.Respawn, RoleSpawnFlags.None);

        player.IsGodModeEnabled = true;
        yield return Timing.WaitForSeconds(2f);
        player.IsGodModeEnabled = false;
    }

    public void OnDied(DiedEventArgs ev)
    {
        Pickup.List.Where(x => x.PreviousOwner == ev.Player).ToList().ForEach(pickup => pickup.Destroy());
        Timing.RunCoroutine(Respawn(ev.Player, 3));
    }

    public void OnInteractingElevator(InteractingElevatorEventArgs ev)
    {
        if (_runGame)
            ev.IsAllowed = false;
    }

    public void OnRoundRestarting()
    {
        _pointAPlayers.Clear();
        _pointBPlayers.Clear();
    }
}