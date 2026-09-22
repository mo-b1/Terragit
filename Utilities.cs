//Utilities.cs
using System;
using System.Security.Cryptography;

namespace Terragit
{
    public static class Utils 
    {
        public static string Hash(byte[] data)
        {
            return Convert.ToHexString(
                SHA256.HashData(data)
            );
        }
    }
}