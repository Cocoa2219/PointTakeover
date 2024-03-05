using System.ComponentModel;
using Exiled.API.Enums;
using Exiled.API.Interfaces;

namespace PointTakeOver
{
    public class Config: IConfig
    {
        public bool IsEnabled { get; set; } = true;
        public bool Debug { get; set; } = false;

        [Description("게임을 진행할 구역을 선택합니다. (LightContainment, HeavyContainment, Entrance)")]
        public ZoneType ZoneType { get; set; } = ZoneType.LightContainment;

        [Description("게임 진행 시간을 설정합니다. (초)")]
        public int GameTime { get; set; } = 300;

        [Description("점령할 때 1명을 기준으로 점령 시간을 설정합니다. (초)")]
        public int OccupyTime { get; set; } = 20;

        [Description("이미 점령된 방에 들어갔을 때 쟁탈 시간을 설정합니다. (초)")]
        public int OccupyStealTime { get; set; } = 12;

        [Description("점령을 계산할 거리의 최소값을 설정합니다. (방에 들어왔는데도 점령이 되지 않는다면 값을 늘리세요.) 크게 정할수록 상당히 서버에 부하를 줄 수 있습니다. 기본값으로 두는 것을 추천합니다. (기본값: 15)")]
        public float CalculateDistance { get; set; } = 15f;
    }
}