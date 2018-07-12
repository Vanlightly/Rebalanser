using System;

namespace Rebalanser.Core
{
    public class Providers
    {
        private static IRebalanserProvider RebalanserProvider;

        public static void Register(IRebalanserProvider rebalanserProvider)
        {
            RebalanserProvider = rebalanserProvider;
        }

        public static IRebalanserProvider GetProvider()
        {
            if (RebalanserProvider == null)
                throw new ProviderException("No provider registered!");

            return RebalanserProvider;
        }
    }
}
