using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PickAndPlace.Models
{
    enum StatusState
    {
        Ok = 0,
        Ng = 1,
        Inspecting = 3,
        Unknown = 2,
        Stopped = 4
    }
    public enum SaveType
    {
        ORIGINAL_RESULT = 0,
        RESULT = 1,
        ORIGINAL = 2
    }
    public enum LogLevel
    {
        Info,
        Success,
        Warning,
        Error
    }
}
