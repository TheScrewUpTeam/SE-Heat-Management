using System.Text;

namespace TSUT.HeatManagement
{
    public interface IEventControllerEvent
    {
        void UpdateDetailedInfo(long entityId);
        void UpdateSettings(long entityId, float treshholdValue);
    }
}