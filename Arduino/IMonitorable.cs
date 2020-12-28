using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Chetch.Database;
using Chetch.Messaging;

namespace Chetch.Arduino
{
    public interface IMonitorable
    {
        void Initialise(DB db);
        void Monitor(DB db, List<Message> messages, bool returnEventsOnly);
        void LogState(DB db);
    }
}
