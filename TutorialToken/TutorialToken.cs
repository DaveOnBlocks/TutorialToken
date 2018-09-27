using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;

namespace TutorialToken
{
    public class TutorialToken : SmartContract
    {
        public static readonly byte[] Owner = "AK2nJJpJr6o664CWJKi1QRXjqeic2zRp8y".ToScriptHash();
        public static readonly byte[] KycAdministrator = "AGE6Z6rgm4Lt9S864Jg2RvfCfQXa34XscF".ToScriptHash();
        private static readonly byte[] NeoAssetId = { 155, 124, 255, 218, 166, 116, 190, 174, 15, 147, 14, 190, 96, 133, 175, 144, 147, 229, 254, 86, 179, 74, 92, 34, 12, 205, 207, 110, 252, 51, 111, 197 };
        private static readonly byte[] GasAssetId = { 231, 45, 40, 105, 121, 238, 108, 177, 183, 230, 93, 253, 223, 178, 227, 132, 16, 11, 141, 20, 142, 119, 88, 222, 66, 228, 22, 139, 113, 121, 44, 96 };

        [DisplayName("transfer")]
        public static event Action<byte[], byte[], BigInteger> Transferred;

        public static object Main(string method, params object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                if (Runtime.CheckWitness(Owner))
                    return true;

                return CanSenderContribute();
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                PerformFundTransferCheck();
                
                if (method == "name")
                    return "Tutorial Token";
                if (method == "symbol")
                    return "TT";
                if (method == "decimals")
                    return 2;
                if (method == "totalSupply")
                    return 1000_00;
                if (method == "initialize")
                    Initialize();
                if (method == "balanceOf")
                    return BalanceOf((byte[]) args[0]);
                if (method == "transfer")
                    return Transfer((byte[]) args[0], (byte[]) args[1], (BigInteger) args[2]);
                if (method == "startSale")
                    return StartSale((BigInteger) args[0], (BigInteger) args[1]);
                if (method == "kycRegister")
                    return KycRegister((byte[]) args[0]);
                if (method == "mintTokens")
                    return MintTokens();
                return false;
            }
            else
            {
                Runtime.Notify("Unsupported trigger type");
                return false;
            }
        }

        public static bool MintTokens()
        {
            var sender = GetSender();
            var neoContributeValue = GetSentToContract(NeoAssetId);
            var gasContributeValue = GetSentToContract(GasAssetId);

            var balance = Storage.Get(Storage.CurrentContext, sender).AsBigInteger();

            BigInteger newTokens = 0; 
            if (neoContributeValue > 0)
            {
                var swapRate = (ulong)2;
                newTokens += neoContributeValue / swapRate;
            }

            if (gasContributeValue > 0)
            {
                var swapRate = (ulong)5;
                newTokens += neoContributeValue / swapRate;
            }

            Storage.Put(Storage.CurrentContext, sender, balance + newTokens);

            Transferred(null, sender, newTokens);

            return true;
        }

        private static bool CanSenderContribute()
        {
            if (!IsSaleOpen())
            {
                Runtime.Notify("Sale is not open");
                return false;
            }

//            if (!IsKycVerified())
//            {
//                Runtime.Notify("KYC Failure");
//                return false;
//            }

            var neoContributeValue = GetSentToContract(NeoAssetId);
            var gasContributeValue = GetSentToContract(GasAssetId);
            if (neoContributeValue == 0 && gasContributeValue == 0) //if this is a withdrawl attempt, fail the contract (owner check will superceed this)
                return false;

            return true; //sale is open + KYC passed
        }

        private static void PerformFundTransferCheck()
        {
            if (CanSenderContribute()) //sale is open and user is whitelisted, accept the payment
                return;

            //if we get to here, they have sent funds but sale is closes / whitelist failed. Need to determine what to manually refund
            var sender = GetSender();
            var neoContributeValue = GetSentToContract(NeoAssetId);
            var gasContributeValue = GetSentToContract(GasAssetId);

            if (neoContributeValue > 0 && sender.Length != 0) Runtime.Notify("Refund", "Neo", neoContributeValue, sender);
            if (gasContributeValue > 0 && sender.Length != 0) Runtime.Notify("Refund", "Gas", gasContributeValue, sender);
        }

        private static ulong GetSentToContract(byte[] assetId)
        {
            var tx = (Transaction)ExecutionEngine.ScriptContainer;
            var outputs = tx.GetOutputs();
            ulong value = 0;
            foreach (var output in outputs)
            {
                if (output.ScriptHash == ExecutionEngine.ExecutingScriptHash && output.AssetId == assetId) value += (ulong)output.Value;
            }

            return value;
        }

        private static bool IsKycVerified()
        {
            var sender = GetSender();
            if (Storage.Get(Storage.CurrentContext, string.Concat("kyc_", sender)) != null)
                return true;
            return false;
        }

        private static byte[] GetSender()
        {
            var tx = (Transaction)ExecutionEngine.ScriptContainer;
            var reference = tx.GetReferences();
            foreach (var output in reference)
            {
                if (output.AssetId == NeoAssetId) return output.ScriptHash;
                if (output.AssetId == GasAssetId) return output.ScriptHash;
            }
            Runtime.Notify("no output matching that hash was found");
            return new byte[] { };
        }

        public static bool KycRegister(byte[] address)
        {
            if (!Runtime.CheckWitness(KycAdministrator))
                return false;

            Storage.Put(Storage.CurrentContext, string.Concat("kyc_", address), 1);
            return true;
        }

        public static bool Transfer(byte[] from, byte[] to, BigInteger amount)
        {
            if (from.Length != 20)
                throw new Exception("Invalid From Address");
            if (to.Length != 20)
                throw new Exception("Invalid To Address");
            if (amount < 0)
                throw new Exception("Invalid Amount");

            if (!Runtime.CheckWitness(from))
            {
                Runtime.Notify("Address check failed", from, to, amount);
                return false;
            }

            if (amount == 0)
            {
                Transfer(from, to, amount);
                return true;
            }

            if (from == to)
            {
                Transfer(from, to, amount);
                return true;
            }

            var fromBalance = Storage.Get(Storage.CurrentContext, from).AsBigInteger();
            var toBalance = Storage.Get(Storage.CurrentContext, to).AsBigInteger();

            if (fromBalance - amount < 0)
                return false;

            Storage.Put(Storage.CurrentContext, from, fromBalance - amount);
            Storage.Put(Storage.CurrentContext, to, toBalance + amount);
           
            Transferred(from, to, amount);

            return true;
        }

        public static object BalanceOf(byte[] address)
        {
            if (address.Length != 20)
                throw new Exception("Invalid Address");

            return Storage.Get(Storage.CurrentContext, address);
        }

        public static void Initialize()
        {
            if (Runtime.CheckWitness(Owner))
            {
                var initialized = Storage.Get(Storage.CurrentContext, "initialized");
                if (initialized == null)
                {
                    Storage.Put(Storage.CurrentContext, Owner, 1000_00);
                    Storage.Put(Storage.CurrentContext, "initialized", "true");
                    Transferred(null, Owner, 1000_00);
                }
            }
        }

        public static bool StartSale(BigInteger contractStart, BigInteger contractEnd)
        {
            if (!Runtime.CheckWitness(Owner))
            {
                Runtime.Notify("Not authorized");
                return false;
            }
            if (contractEnd <= contractStart)
            {
                Runtime.Notify("Invalid Start/End");
                return false;
            }

            Storage.Put(Storage.CurrentContext, "SaleStart", contractStart);
            Storage.Put(Storage.CurrentContext, "SaleEnd", contractEnd);
            Runtime.Notify("Sale Start/End Recorded");
            return true;
        }

        private static bool IsSaleOpen()
        {
            var start = new BigInteger(Storage.Get(Storage.CurrentContext, "SaleStart"));
            var end = new BigInteger(Storage.Get(Storage.CurrentContext, "SaleEnd"));
            var currentBlock = Blockchain.GetHeight();
            return start > currentBlock && end < currentBlock;
        }
    }
}