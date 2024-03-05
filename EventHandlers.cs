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
    private int _pointAStealTime;
    private int _pointBStealTime;

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

        _takeoverPointA.Color = new Color32(132, 191, 133, 25);
        _takeoverPointB.Color = new Color32(132, 191, 133, 25);
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

        yield return Timing.WaitForSeconds(10f);

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
            var dBoyColor = new Color32(239,121,4, 25);

            var defaultColor = new Color32(132, 191, 133, 25);

            var ntfColor = new Color32(7,143,243,25);
            if (-PointTakeOver.Instance.Config.OccupyTime < _pointAOccupyTime && _pointAOccupyTime < PointTakeOver.Instance.Config.OccupyTime)
            {
                if (_pointAPlayers["A"].Count > 0)
                {
                    if (_pointAPlayers["B"].Count == 0)
                    {
                        Log.Debug($"TeamA가 포인트 A 점령 중... {_pointAOccupyTime}");

                        if (_pointAOccupyTime < 0)
                        {
                            _pointAOccupyTime = 0;
                        }

                        foreach (var player in _pointAPlayers["A"])
                        {
                            _pointAOccupyTime++;
                        }

                        if (_pointAOccupyTime >= PointTakeOver.Instance.Config.OccupyTime)
                        {
                            _pointAOccupyTime = PointTakeOver.Instance.Config.OccupyTime;
                        }

                        var percentage = _pointAOccupyTime / (float) PointTakeOver.Instance.Config.OccupyTime;

                        foreach (var player in _pointAPlayers["A"])
                        {
                            player.ShowHint($"<b>포인트 A를 점령 중... ({Mathf.FloorToInt(percentage * 100)}%)</b>", 2f);
                        }

                        _takeoverPointA.Color = MixColors(dBoyColor, defaultColor, 1 - percentage);

                        if (_pointAOccupyTime >= PointTakeOver.Instance.Config.OccupyTime)
                        {
                            Map.Broadcast(5, $"<size=35><b>포인트 A가 {MakeGradientText("Class-D", new Color32(239,121,4, 255), new Color32(85,38,0,255))} 팀에 의해 점령되었습니다!</b></size>", Broadcast.BroadcastFlags.Normal, true);
                        }
                    }
                    else
                    {
                        Log.Debug("포인트 A에서 대치 중...");
                        _pointAOccupyTime = 0;

                        foreach (var player in _pointAPlayers["A"])
                        {
                            player.ShowHint("<b>대치 중!</b>", 2f);
                        }

                        foreach (var player in _pointAPlayers["B"])
                        {
                            player.ShowHint("<b>대치 중!</b>", 2f);
                        }

                        _takeoverPointA.Color = defaultColor;
                    }
                }
                else if (_pointAPlayers["B"].Count > 0)
                {
                    if (_pointAPlayers["A"].Count == 0)
                    {
                        Log.Debug($"TeamB가 포인트 A 점령 중... {_pointAOccupyTime}");

                        if (_pointAOccupyTime > 0)
                        {
                            _pointAOccupyTime = 0;
                        }

                        foreach (var player in _pointAPlayers["B"])
                        {
                            _pointAOccupyTime--;
                        }

                        if (_pointAOccupyTime <= -PointTakeOver.Instance.Config.OccupyTime)
                        {
                            _pointAOccupyTime = -PointTakeOver.Instance.Config.OccupyTime;
                        }

                        var percentage = -_pointAOccupyTime / (float) PointTakeOver.Instance.Config.OccupyTime;

                        foreach (var player in _pointAPlayers["B"])
                        {
                            player.ShowHint($"<b>포인트 A를 점령 중... ({Mathf.FloorToInt(percentage * 100)}%)</b>", 2f);
                        }

                        _takeoverPointA.Color = MixColors(ntfColor, defaultColor, 1 - percentage);

                        if (_pointAOccupyTime <= -PointTakeOver.Instance.Config.OccupyTime)
                        {
                            Map.Broadcast(5, $"<size=35><b>포인트 A가 {MakeGradientText("Nine-Tailed-Fox", new Color32(7,143,243,255), new Color32(0,46,85,255))} 팀에 의해 점령되었습니다!</b></size>", Broadcast.BroadcastFlags.Normal, true);
                        }
                    }
                    else
                    {
                        Log.Debug("포인트 A에서 대치 중...");
                        _pointAOccupyTime = 0;
                        foreach (var player in _pointAPlayers["A"])
                        {
                            player.ShowHint("<b>대치 중!</b>", 2f);
                        }

                        foreach (var player in _pointAPlayers["B"])
                        {
                            player.ShowHint("<b>대치 중!</b>", 2f);
                        }

                        _takeoverPointA.Color = defaultColor;
                    }
                }
                else
                {
                    _pointAOccupyTime = 0;
                    _takeoverPointA.Color = defaultColor;
                }
            }
            else
            {
                if (-PointTakeOver.Instance.Config.OccupyTime == _pointAOccupyTime)
                {
                    if (_pointAPlayers["A"].Count > 0)
                        if (_pointAPlayers["B"].Count == 0)
                        {
                            Log.Debug("TeamA가 포인트 A 쟁탈 중...");

                            foreach (var player in _pointAPlayers["A"])
                            {
                                _pointAStealTime++;
                            }

                            if (_pointAStealTime >= PointTakeOver.Instance.Config.OccupyStealTime)
                            {
                                _pointAStealTime = PointTakeOver.Instance.Config.OccupyStealTime;
                            }

                            var percentage = _pointAStealTime / (float) PointTakeOver.Instance.Config.OccupyStealTime;

                            _takeoverPointA.Color = MixColors(ntfColor, defaultColor, percentage);

                            foreach (var player in _pointAPlayers["A"])
                            {
                                player.ShowHint($"<b>포인트 A를 쟁탈 중... ({Mathf.FloorToInt(percentage * 100)}%)</b>", 2f);
                            }

                            if (_pointAStealTime >= PointTakeOver.Instance.Config.OccupyStealTime)
                            {
                                _pointAStealTime = 0;
                                _pointAOccupyTime = 0;
                                Map.Broadcast(5,
                                    $"<size=35><b>포인트 A가 {MakeGradientText("Class-D", new Color32(239, 121, 4, 255), new Color32(85, 38, 0, 255))} 팀에 의해 쟁탈되었습니다!</b></size>",
                                    Broadcast.BroadcastFlags.Normal, true);
                            }
                        }
                        else
                        {
                            _pointAStealTime = 0;
                            foreach (var player in _pointAPlayers["A"])
                            {
                                player.ShowHint("<b>대치 중!</b>", 2f);
                            }

                            foreach (var player in _pointAPlayers["B"])
                            {
                                player.ShowHint("<b>대치 중!</b>", 2f);
                            }

                            _takeoverPointA.Color = ntfColor;
                        }
                    else
                    {
                        _pointAStealTime = 0;

                        _takeoverPointA.Color = ntfColor;
                    }
                }
                else if (PointTakeOver.Instance.Config.OccupyTime == _pointAOccupyTime)
                {
                    if (_pointAPlayers["B"].Count > 0)
                        if (_pointAPlayers["A"].Count == 0)
                        {
                            Log.Debug("TeamB가 포인트 A 쟁탈 중...");

                            foreach (var player in _pointAPlayers["B"])
                            {
                                _pointAStealTime--;
                            }

                            if (_pointAStealTime <= -PointTakeOver.Instance.Config.OccupyStealTime)
                            {
                                _pointAStealTime = -PointTakeOver.Instance.Config.OccupyStealTime;
                            }

                            var percentage = -_pointAStealTime / (float) PointTakeOver.Instance.Config.OccupyStealTime;

                            _takeoverPointA.Color = MixColors(dBoyColor, defaultColor, percentage);

                            foreach (var player in _pointAPlayers["B"])
                            {
                                player.ShowHint($"<b>포인트 A를 쟁탈 중... ({Mathf.FloorToInt(percentage * 100)}%)</b>", 2f);
                            }

                            if (_pointAStealTime <= -PointTakeOver.Instance.Config.OccupyStealTime)
                            {
                                _pointAStealTime = 0;
                                _pointAOccupyTime = 0;
                                Map.Broadcast(5,
                                    $"<size=35><b>포인트 A가 {MakeGradientText("Nine-Tailed-Fox", new Color32(7, 143, 243, 255), new Color32(0, 46, 85, 255))} 팀에 의해 쟁탈되었습니다!</b></size>",
                                    Broadcast.BroadcastFlags.Normal, true);
                            }
                        }
                        else
                        {
                            _pointAStealTime = 0;
                            foreach (var player in _pointAPlayers["A"])
                            {
                                player.ShowHint("<b>대치 중!</b>", 2f);
                            }

                            foreach (var player in _pointAPlayers["B"])
                            {
                                player.ShowHint("<b>대치 중!</b>", 2f);
                            }

                            _takeoverPointA.Color = dBoyColor;
                        }
                    else
                    {
                        _pointAStealTime = 0;
                        _takeoverPointA.Color = dBoyColor;
                    }
                }
            }

            // point B
            if (-PointTakeOver.Instance.Config.OccupyTime < _pointBOccupyTime && _pointBOccupyTime < PointTakeOver.Instance.Config.OccupyTime)
            {
                if (_pointBPlayers["A"].Count > 0)
                {
                    if (_pointBPlayers["B"].Count == 0)
                    {
                        Log.Debug($"TeamA가 포인트 B 점령 중... {_pointBOccupyTime}");

                        if (_pointBOccupyTime < 0)
                        {
                            _pointBOccupyTime = 0;
                        }

                        foreach (var player in _pointBPlayers["A"])
                        {
                            _pointBOccupyTime++;
                        }

                        if (_pointBOccupyTime >= PointTakeOver.Instance.Config.OccupyTime)
                        {
                            _pointBOccupyTime = PointTakeOver.Instance.Config.OccupyTime;
                        }

                        var percentage = _pointBOccupyTime / (float) PointTakeOver.Instance.Config.OccupyTime;

                        foreach (var player in _pointBPlayers["A"])
                        {
                            player.ShowHint($"<b>포인트 B를 점령 중... ({Mathf.FloorToInt(percentage * 100)}%)</b>", 2f);
                        }

                        _takeoverPointB.Color = MixColors(dBoyColor, defaultColor, 1 - percentage);

                        if (_pointBOccupyTime >= PointTakeOver.Instance.Config.OccupyTime)
                        {
                            Map.Broadcast(5, $"<size=35><b>포인트 B가 {MakeGradientText("Class-D", new Color32(239,121,4, 255), new Color32(85,38,0,255))} 팀에 의해 점령되었습니다!</b></size>", Broadcast.BroadcastFlags.Normal, true);
                        }
                    }
                    else
                    {
                        Log.Debug("포인트 B에서 대치 중...");
                        _pointBOccupyTime = 0;
                        foreach (var player in _pointBPlayers["A"])
                        {
                            player.ShowHint("<b>대치 중!</b>", 2f);
                        }

                        foreach (var player in _pointBPlayers["B"])
                        {
                            player.ShowHint("<b>대치 중!</b>", 2f);
                        }

                        _takeoverPointB.Color = defaultColor;
                    }
                }
                else if (_pointBPlayers["B"].Count > 0)
                {
                    if (_pointBPlayers["A"].Count == 0)
                    {
                        Log.Debug($"TeamB가 포인트 B 점령 중... {_pointBOccupyTime}");

                        if (_pointBOccupyTime > 0)
                        {
                            _pointBOccupyTime = 0;
                        }

                        foreach (var player in _pointBPlayers["B"])
                        {
                            _pointBOccupyTime--;
                        }

                        if (_pointBOccupyTime <= -PointTakeOver.Instance.Config.OccupyTime)
                        {
                            _pointBOccupyTime = -PointTakeOver.Instance.Config.OccupyTime;
                        }

                        var percentage = -_pointBOccupyTime / (float) PointTakeOver.Instance.Config.OccupyTime;

                        _takeoverPointB.Color = MixColors(ntfColor, defaultColor, 1 - percentage);

                        foreach (var player in _pointBPlayers["B"])
                        {
                            player.ShowHint($"<b>포인트 B를 점령 중... ({Mathf.FloorToInt(percentage * 100)}%)</b>", 2f);
                        }

                        if (_pointBOccupyTime <= -PointTakeOver.Instance.Config.OccupyTime)
                        {
                            Map.Broadcast(5, $"<size=35><b>포인트 B가 {MakeGradientText("Nine-Tailed-Fox", new Color32(7,143,243,255), new Color32(0,46,85,255))} 팀에 의해 점령되었습니다!</b></size>", Broadcast.BroadcastFlags.Normal, true);
                        }
                    }
                    else
                    {
                        Log.Debug("포인트 B에서 대치 중...");
                        _pointBOccupyTime = 0;
                        foreach (var player in _pointBPlayers["A"])
                        {
                            player.ShowHint("<b>대치 중!</b>", 2f);
                        }

                        foreach (var player in _pointBPlayers["B"])
                        {
                            player.ShowHint("<b>대치 중!</b>", 2f);
                        }

                        _takeoverPointB.Color = defaultColor;
                    }
                }
                else
                {
                    _pointBOccupyTime = 0;
                    _takeoverPointB.Color = defaultColor;
                }
            }
            else
            {
                if (-PointTakeOver.Instance.Config.OccupyTime == _pointBOccupyTime)
                {
                    if (_pointBPlayers["A"].Count > 0)
                        if (_pointBPlayers["B"].Count == 0)
                        {
                            Log.Debug("TeamA가 포인트 B 쟁탈 중...");

                            foreach (var player in _pointBPlayers["A"])
                            {
                                _pointBStealTime++;
                            }

                            if (_pointBStealTime >= PointTakeOver.Instance.Config.OccupyStealTime)
                            {
                                _pointBStealTime = PointTakeOver.Instance.Config.OccupyStealTime;
                            }

                            var percentage = _pointBStealTime / (float)PointTakeOver.Instance.Config.OccupyStealTime;

                            _takeoverPointB.Color = MixColors(ntfColor, defaultColor, percentage);

                            foreach (var player in _pointBPlayers["A"])
                            {
                                player.ShowHint($"<b>포인트 B를 쟁탈 중... ({Mathf.FloorToInt(percentage * 100)}%)</b>", 2f);
                            }

                            if (_pointBStealTime >= PointTakeOver.Instance.Config.OccupyStealTime)
                            {
                                _pointBStealTime = 0;
                                _pointBOccupyTime = 0;
                                Map.Broadcast(5,
                                    $"<size=35><b>포인트 B가 {MakeGradientText("Class-D", new Color32(239, 121, 4, 255), new Color32(85, 38, 0, 255))} 팀에 의해 쟁탈되었습니다!</b></size>",
                                    Broadcast.BroadcastFlags.Normal, true);
                            }
                        }
                        else
                        {
                            _pointBStealTime = 0;
                            foreach (var player in _pointBPlayers["A"])
                            {
                                player.ShowHint("<b>대치 중!</b>", 2f);
                            }

                            foreach (var player in _pointBPlayers["B"])
                            {
                                player.ShowHint("<b>대치 중!</b>", 2f);
                            }

                            _takeoverPointB.Color = ntfColor;
                        }
                    else
                    {
                        _pointBStealTime = 0;
                        _takeoverPointB.Color = ntfColor;
                    }
                }
                else if (PointTakeOver.Instance.Config.OccupyTime == _pointBOccupyTime)
                {
                    if (_pointBPlayers["B"].Count > 0)
                        if (_pointBPlayers["A"].Count == 0)
                        {
                            Log.Debug("TeamB가 포인트 B 쟁탈 중...");

                            foreach (var player in _pointBPlayers["B"])
                            {
                                _pointBStealTime--;
                            }

                            if (_pointBStealTime <= -PointTakeOver.Instance.Config.OccupyStealTime)
                            {
                                _pointBStealTime = -PointTakeOver.Instance.Config.OccupyStealTime;
                            }

                            var percentage = -_pointBStealTime / (float)PointTakeOver.Instance.Config.OccupyStealTime;

                            _takeoverPointB.Color = MixColors(dBoyColor, defaultColor, percentage);

                            foreach (var player in _pointBPlayers["B"])
                            {
                                player.ShowHint($"<b>포인트 B를 쟁탈 중... ({Mathf.FloorToInt(percentage * 100)}%)</b>", 2f);
                            }

                            if (_pointBStealTime <= -PointTakeOver.Instance.Config.OccupyStealTime)
                            {
                                _pointBStealTime = 0;
                                _pointBOccupyTime = 0;
                                Map.Broadcast(5,
                                    $"<size=35><b>포인트 B가 {MakeGradientText("Nine-Tailed-Fox", new Color32(7, 143, 243, 255), new Color32(0, 46, 85, 255))} 팀에 의해 쟁탈되었습니다!</b></size>",
                                    Broadcast.BroadcastFlags.Normal, true);
                            }
                        }
                        else
                        {
                            _pointBStealTime = 0;
                            foreach (var player in _pointBPlayers["A"])
                            {
                                player.ShowHint("<b>대치 중!</b>", 2f);
                            }

                            foreach (var player in _pointBPlayers["B"])
                            {
                                player.ShowHint("<b>대치 중!</b>", 2f);
                            }

                            _takeoverPointB.Color = dBoyColor;
                        }
                    else
                    {
                        _pointBStealTime = 0;
                        _takeoverPointB.Color = dBoyColor;
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

        var spawnableRooms = Room.List.Where(x =>
            _playerSpawnRooms[PointTakeOver.Instance.Config.ZoneType].Contains(x.Type) && x != _takeoverPointA &&
            x != _takeoverPointB).ToList();

        if (_teamA.Contains(player))
        {
            player.Role.Set(RoleTypeId.ClassD, SpawnReason.Respawn, RoleSpawnFlags.None);
        }
        else if (_teamB.Contains(player))
        {
            player.Role.Set(RoleTypeId.NtfSergeant, SpawnReason.Respawn, RoleSpawnFlags.None);
        }

        spawnableRooms.ShuffleList();
        player.Position = spawnableRooms.First().Position + new Vector3(0, 1, 0);

        player.AddItem(ItemType.GunE11SR);
        player.AddItem(ItemType.Medkit);
        player.AddItem(ItemType.Adrenaline);
        player.AddItem(ItemType.ArmorCombat);

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