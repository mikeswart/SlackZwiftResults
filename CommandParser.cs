using System;
using System.Linq;

namespace Dropouts.ZwiftResults
{
    public static class CommandParser
    {
        public static bool TryParse(string input, params (string commandName, Action<string[]> action)[] commands)
        {
            var tokens = input.Split(' ');
            if(tokens.Length <= 0)
            {
                return false;
            }

            if(commands.ToDictionary(pair => pair.commandName, pair => pair.action).TryGetValue(tokens.First(), out var action))
            {
                action(tokens.Skip(1).ToArray());
                return true;
            }

            return false;
        }
    }
}
