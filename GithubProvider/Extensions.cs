using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GithubProvider
{
    internal static class Extensions
    {
        public static T Resolve<T>(this Task<T> task)
        {
            return task.GetAwaiter().GetResult();
        }
    }
}
