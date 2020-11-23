using System.Numerics;
using Nethereum.Signer;
using Nethereum.Util;
using Nethereum.Web3.Accounts;
using Nethereum.XUnitEthereumClients;
using Xunit;
using Nethereum.WalletForwarder.Contracts.Forwarder;
using Nethereum.WalletForwarder.Contracts.Forwarder.ContractDefinition;
using Nethereum.WalletForwarder.Contracts.ForwarderFactory;
using Nethereum.WalletForwarder.Contracts.ForwarderFactory.ContractDefinition;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.RLP;
using Nethereum.Contracts;
using Nethereum.ABI.Encoders;
using Nethereum.ABI.Decoders;
using System.Globalization;
using System.Collections.Generic;
using Nethereum.WalletForwarder.Contracts.ERC20Token;
using Nethereum.WalletForwarder.Contracts.ERC20Token.ContractDefinition;
using System.Threading.Tasks;
using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Nethereum.Forwarder.IntegrationTests
{
    [Collection(EthereumClientIntegrationFixture.ETHEREUM_CLIENT_COLLECTION_DEFAULT)]
    public class ForwarderTests

    {
        private readonly EthereumClientIntegrationFixture _ethereumClientIntegrationFixture;

        public ForwarderTests(EthereumClientIntegrationFixture ethereumClientIntegrationFixture)
        {
            _ethereumClientIntegrationFixture = ethereumClientIntegrationFixture;
        }

        [Fact]
        public void Test()
        {
            Assert.True(true);
        }

        [Fact]
        public async void ShouldDeployForwarder_CloneItUsingFactory_TransferEther()
        {
            var destinationAddress = "0x6C547791C3573c2093d81b919350DB1094707011";
            var web3 = _ethereumClientIntegrationFixture.GetWeb3();

            //Getting the current Ether balance of the destination, we are going to transfer 0.001 ether
            var balanceDestination = await web3.Eth.GetBalance.SendRequestAsync(destinationAddress);
            var balanceDestinationEther = Web3.Web3.Convert.FromWei(balanceDestination);

            var defaultForwarderDeploymentReceipt = await ForwarderService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderDeployment());
            var defaultForwaderContractAddress = defaultForwarderDeploymentReceipt.ContractAddress;
            var defaultForwarderService =  new ForwarderService(web3, defaultForwaderContractAddress);
            await defaultForwarderService.ChangeDestinationRequestAndWaitForReceiptAsync(destinationAddress);
            var destinationInContract = await defaultForwarderService.DestinationQueryAsync();
            Assert.True(destinationInContract.IsTheSameAddress(destinationAddress));

            var factoryDeploymentReceipt = await ForwarderFactoryService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderFactoryDeployment());
            var factoryAddress = factoryDeploymentReceipt.ContractAddress;
            var factoryService = new ForwarderFactoryService(web3, factoryDeploymentReceipt.ContractAddress);

            //New invovice 
            var salt = BigInteger.Parse("12");
            var saltHex = new IntTypeEncoder().Encode(salt).ToHex();

            var contractCalculatedAddress = CalculateCreate2AddressMinimalProxy(factoryAddress, saltHex, defaultForwaderContractAddress);
            var txnReceipt = await factoryService.CloneForwarderRequestAndWaitForReceiptAsync(defaultForwaderContractAddress, salt);
            var clonedAdress = txnReceipt.DecodeAllEvents<ForwarderClonedEventDTO>()[0].Event.ClonedAdress;
            Assert.True(clonedAdress.IsTheSameAddress(contractCalculatedAddress));


            var clonedForwarderService = new ForwarderService(web3, contractCalculatedAddress);
            var destinationInContractCloned = await clonedForwarderService.DestinationQueryAsync();
            Assert.True(destinationInContractCloned.IsTheSameAddress(destinationAddress));

            //gas is added due to forwarding
            var transferEtherReceipt = await web3.Eth.GetEtherTransferService().TransferEtherAndWaitForReceiptAsync(contractCalculatedAddress, 10, null, 4500000);
           // var forwardedDeposit = transferEtherReceipt.DecodeAllEvents<ForwarderDepositedEventDTO>()[0].Event;

            var balance = await web3.Eth.GetBalance.SendRequestAsync(destinationAddress);
            Assert.Equal(balanceDestinationEther + 10, Web3.Web3.Convert.FromWei(balance));
            var balance2 = await web3.Eth.GetBalance.SendRequestAsync(contractCalculatedAddress);
            Assert.Equal(0, balance2.Value);

        }

        [Fact]
        public async void ShouldDeployForwarder_TransferEther_CloneItUsingFactory_FlushEther()
        {
            var destinationAddress = "0x6C547791C3573c2093d81b919350DB1094707011";
            //Using ropsten infura 
            //var web3 = _ethereumClientIntegrationFixture.GetInfuraWeb3(InfuraNetwork.Ropsten);
            var web3 = _ethereumClientIntegrationFixture.GetWeb3(); 

            //Getting the current Ether balance of the destination, we are going to transfer 0.001 ether
            var balanceDestination = await web3.Eth.GetBalance.SendRequestAsync(destinationAddress);
            var balanceDestinationEther = Web3.Web3.Convert.FromWei(balanceDestination);
            
            //Deploying first the default forwarder (template for all clones)
            var defaultForwarderDeploymentReceipt = await ForwarderService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderDeployment());
            var defaultForwaderContractAddress = defaultForwarderDeploymentReceipt.ContractAddress;
            var defaultForwarderService = new ForwarderService(web3, defaultForwaderContractAddress);
            //initialiasing with the destination address
            await defaultForwarderService.ChangeDestinationRequestAndWaitForReceiptAsync(destinationAddress);
            var destinationInContract = await defaultForwarderService.DestinationQueryAsync();
            //validate the destination address has been set correctly
            Assert.True(destinationInContract.IsTheSameAddress(destinationAddress));
            
            //Deploying the factory
            var factoryDeploymentReceipt = await ForwarderFactoryService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderFactoryDeployment());
            var factoryAddress = factoryDeploymentReceipt.ContractAddress;
            var factoryService = new ForwarderFactoryService(web3, factoryDeploymentReceipt.ContractAddress);

            //Lets create new invovice to be paid
            var salt = BigInteger.Parse("12"); //12 our invoice number
            var saltHex = new IntTypeEncoder().Encode(salt).ToHex();

            //Calculate the new contract address
            var contractCalculatedAddress = CalculateCreate2AddressMinimalProxy(factoryAddress, saltHex, defaultForwaderContractAddress);
          
            //Let's tranfer some ether, with some extra gas to allow forwarding if the smart contract is deployed (UX problem)
            var transferEtherReceipt = await web3.Eth.GetEtherTransferService().TransferEtherAndWaitForReceiptAsync(contractCalculatedAddress, (decimal)0.001, null, 4500000);
            

            //Check the balance of the adress we sent.. we have not deployed the smart contract so it should be still the same
            var balanceContract = await web3.Eth.GetBalance.SendRequestAsync(contractCalculatedAddress);
            //Assert.Equal((decimal)0.001, Web3.Web3.Convert.FromWei(balanceContract.Value));

            //Create the clone with the salt to match the address
            var txnReceipt = await factoryService.CloneForwarderRequestAndWaitForReceiptAsync(defaultForwaderContractAddress, salt);
            var clonedAdress = txnReceipt.DecodeAllEvents<ForwarderClonedEventDTO>()[0].Event.ClonedAdress;
            Assert.True(clonedAdress.IsTheSameAddress(contractCalculatedAddress));


            //we should still have the same balance
            balanceContract = await web3.Eth.GetBalance.SendRequestAsync(contractCalculatedAddress);
            Assert.Equal((decimal)0.001, Web3.Web3.Convert.FromWei(balanceContract.Value));

            //create a service to for cloned forwarder
            var clonedForwarderService = new ForwarderService(web3, contractCalculatedAddress);
            var destinationInContractCloned = await clonedForwarderService.DestinationQueryAsync();
            //validate the destination address is the same
            Assert.True(destinationInContractCloned.IsTheSameAddress(destinationAddress));

            //Using flush directly in the cloned contract
            //call flush to get all the ether transferred to destination address 
            var flushReceipt = await clonedForwarderService.FlushRequestAndWaitForReceiptAsync();
            balanceContract = await web3.Eth.GetBalance.SendRequestAsync(contractCalculatedAddress);
            //validate balances...
            var newbalanceDestination = await web3.Eth.GetBalance.SendRequestAsync(destinationAddress);
            Assert.Equal((decimal)0.001 + balanceDestinationEther, Web3.Web3.Convert.FromWei(newbalanceDestination));
        }



        [Fact]
        public async void ShouldDeployForwarder_TransferEther_CloneItUsingFactory_FlushEther2ClonesUsingFactory()
        {
            var destinationAddress = "0x6C547791C3573c2093d81b919350DB1094707011";
            //Using ropsten infura 
            //var web3 = _ethereumClientIntegrationFixture.GetInfuraWeb3(InfuraNetwork.Ropsten);
            var web3 = _ethereumClientIntegrationFixture.GetWeb3();

            //Getting the current Ether balance of the destination, we are going to transfer 0.001 ether
            var balanceDestination = await web3.Eth.GetBalance.SendRequestAsync(destinationAddress);
            var balanceDestinationEther = Web3.Web3.Convert.FromWei(balanceDestination);

            //Deploying first the default forwarder (template for all clones)
            var defaultForwarderDeploymentReceipt = await ForwarderService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderDeployment());
            var defaultForwaderContractAddress = defaultForwarderDeploymentReceipt.ContractAddress;
            var defaultForwarderService = new ForwarderService(web3, defaultForwaderContractAddress);
            //initialiasing with the destination address
            await defaultForwarderService.ChangeDestinationRequestAndWaitForReceiptAsync(destinationAddress);
            var destinationInContract = await defaultForwarderService.DestinationQueryAsync();
            //validate the destination address has been set correctly
            Assert.True(destinationInContract.IsTheSameAddress(destinationAddress));

            //Deploying the factory
            var factoryDeploymentReceipt = await ForwarderFactoryService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderFactoryDeployment());
            var factoryAddress = factoryDeploymentReceipt.ContractAddress;
            var factoryService = new ForwarderFactoryService(web3, factoryDeploymentReceipt.ContractAddress);

            //Lets create new contract to be paid
            var salt = BigInteger.Parse("12"); //salt id
            var saltHex = new IntTypeEncoder().Encode(salt).ToHex();

            //Calculate the new contract address
            var contractCalculatedAddress = CalculateCreate2AddressMinimalProxy(factoryAddress, saltHex, defaultForwaderContractAddress);

            //Let's tranfer some ether, with some extra gas to allow forwarding if the smart contract is deployed (UX problem)
            var transferEtherReceipt = await web3.Eth.GetEtherTransferService().TransferEtherAndWaitForReceiptAsync(contractCalculatedAddress, (decimal)0.001, null, 4500000);


            //Lets create new contract to be paid
            var salt2 = BigInteger.Parse("13"); //salt id
            var saltHex2 = new IntTypeEncoder().Encode(salt2).ToHex();

            //Calculate the new contract address
            var contractCalculatedAddress2 = CalculateCreate2AddressMinimalProxy(factoryAddress, saltHex2, defaultForwaderContractAddress);

            //Let's tranfer some ether, with some extra gas to allow forwarding if the smart contract is deployed (UX problem)
            var transferEtherReceipt2 = await web3.Eth.GetEtherTransferService().TransferEtherAndWaitForReceiptAsync(contractCalculatedAddress2, (decimal)0.001, null, 4500000);



            //Create the clone with the salt to match the address
            var txnReceipt = await factoryService.CloneForwarderRequestAndWaitForReceiptAsync(defaultForwaderContractAddress, salt);
            var clonedAdress = txnReceipt.DecodeAllEvents<ForwarderClonedEventDTO>()[0].Event.ClonedAdress;
            Assert.True(clonedAdress.IsTheSameAddress(contractCalculatedAddress));

            //Create the clone2 with the salt to match the address
            var txnReceipt2 = await factoryService.CloneForwarderRequestAndWaitForReceiptAsync(defaultForwaderContractAddress, salt2);
            var clonedAdress2 = txnReceipt2.DecodeAllEvents<ForwarderClonedEventDTO>()[0].Event.ClonedAdress;
            Assert.True(clonedAdress2.IsTheSameAddress(contractCalculatedAddress2));

           
            //Flushing from the factory
            var flushAllReceipt = await factoryService.FlushEtherRequestAndWaitForReceiptAsync(new List<string> { contractCalculatedAddress, contractCalculatedAddress2 });

            //////validate balances... for two forwarders of 0.001 + 0.001
            var newbalanceDestination = await web3.Eth.GetBalance.SendRequestAsync(destinationAddress);
            Assert.Equal((decimal)0.001 + (decimal)0.001 + balanceDestinationEther, Web3.Web3.Convert.FromWei(newbalanceDestination));
        }



        [Fact]
        public async void ShouldDeployForwarder_TransferEther_CloneItUsingFactory_FlushEtherManyClonesUsingFactory()
        {
            var destinationAddress = "0x6C547791C3573c2093d81b919350DB1094707011";
            //Using ropsten infura 
            //var web3 = _ethereumClientIntegrationFixture.GetInfuraWeb3(InfuraNetwork.Ropsten);
            var web3 = _ethereumClientIntegrationFixture.GetWeb3();

            //Getting the current Ether balance of the destination, we are going to transfer 0.001 ether
            var balanceDestination = await web3.Eth.GetBalance.SendRequestAsync(destinationAddress);
            var balanceDestinationEther = Web3.Web3.Convert.FromWei(balanceDestination);

            //Deploying first the default forwarder (template for all clones)
            var defaultForwarderDeploymentReceipt = await ForwarderService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderDeployment());
            var defaultForwaderContractAddress = defaultForwarderDeploymentReceipt.ContractAddress;
            var defaultForwarderService = new ForwarderService(web3, defaultForwaderContractAddress);
            //initialiasing with the destination address
            await defaultForwarderService.ChangeDestinationRequestAndWaitForReceiptAsync(destinationAddress);
            var destinationInContract = await defaultForwarderService.DestinationQueryAsync();
            //validate the destination address has been set correctly
            Assert.True(destinationInContract.IsTheSameAddress(destinationAddress));

            //Deploying the factory
            var factoryDeploymentReceipt = await ForwarderFactoryService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderFactoryDeployment());
            var factoryAddress = factoryDeploymentReceipt.ContractAddress;
            var factoryService = new ForwarderFactoryService(web3, factoryDeploymentReceipt.ContractAddress);
            var addresses = await SendEtherAndCreateClones(50, web3, factoryService, 0.001M, factoryAddress, defaultForwaderContractAddress);


            //Flushing from the factory
            var flushAllReceipt = await factoryService.FlushEtherRequestAndWaitForReceiptAsync(addresses);
            //check here the cost ^^^
            var totalEtherTransfered = 0.001M * addresses.Count;
          
            var newbalanceDestination = await web3.Eth.GetBalance.SendRequestAsync(destinationAddress);
            Assert.Equal(totalEtherTransfered + balanceDestinationEther, Web3.Web3.Convert.FromWei(newbalanceDestination));
        }

        private async Task<List<string>> SendEtherAndCreateClones(int numberOfClones, Web3.Web3 web3, ForwarderFactoryService factoryService, decimal amount, string factoryAddress, string defaultForwaderContractAddress)
        {

            var numProcs = Environment.ProcessorCount;
            var concurrencyLevel = numProcs * 2;
            var concurrentDictionary = new ConcurrentDictionary<int, string>(concurrencyLevel, numberOfClones * 2);
            var taskItems = new List<int>();
            for (var i = 0; i < numberOfClones; i++)
                taskItems.Add(i);

            Parallel.ForEach(taskItems, (item, state) =>
            {
                var id = item.ToString();
                var address = SendEtherAndCreateClone(web3, factoryService, amount, id, factoryAddress, defaultForwaderContractAddress).Result;
                concurrentDictionary.TryAdd(item, address);
            });

            return concurrentDictionary.Values.ToList();
        }
        

            private async Task<string> SendEtherAndCreateClone(Web3.Web3 web3, ForwarderFactoryService factoryService, decimal amount, string saltNumber, string factoryAddress, string defaultForwaderContractAddress)
        {
            //Lets create new contract to be paid
            var salt = BigInteger.Parse(saltNumber); //salt id
            var saltHex = new IntTypeEncoder().Encode(salt).ToHex();

            //Calculate the new contract address
            var contractCalculatedAddress = CalculateCreate2AddressMinimalProxy(factoryAddress, saltHex, defaultForwaderContractAddress);

            //Let's tranfer some ether, with some extra gas to allow forwarding if the smart contract is deployed (UX problem)
            var transferEtherReceipt = await web3.Eth.GetEtherTransferService().TransferEtherAndWaitForReceiptAsync(contractCalculatedAddress, amount, null, 4500000);
            
            var txnReceipt = await factoryService.CloneForwarderRequestAndWaitForReceiptAsync(defaultForwaderContractAddress, salt);
            var clonedAdress = txnReceipt.DecodeAllEvents<ForwarderClonedEventDTO>()[0].Event.ClonedAdress;
            Assert.True(clonedAdress.IsTheSameAddress(contractCalculatedAddress));
            return contractCalculatedAddress;
        }

        [Fact]
        public async void ShouldDeployForwarder_TransferToken_CloneItUsingFactory_FlushToken()
        {
            var destinationAddress = "0x6C547791C3573c2093d81b919350DB1094707011";
            //Using ropsten infura 
            //var web3 = _ethereumClientIntegrationFixture.GetInfuraWeb3(InfuraNetwork.Ropsten);
            var web3 = _ethereumClientIntegrationFixture.GetWeb3();

            //Deploy our custom token
            var tokenDeploymentReceipt = await ERC20TokenService.DeployContractAndWaitForReceiptAsync(web3,
                new ERC20TokenDeployment() { DecimalUnits = 18, TokenName = "TST", TokenSymbol = "TST", InitialAmount = Web3.Web3.Convert.ToWei(10000) });
            var tokenService = new ERC20TokenService(web3, tokenDeploymentReceipt.ContractAddress);

            //Deploying first the default forwarder (template for all clones)
            var defaultForwarderDeploymentReceipt = await ForwarderService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderDeployment());
            var defaultForwaderContractAddress = defaultForwarderDeploymentReceipt.ContractAddress;
            var defaultForwarderService = new ForwarderService(web3, defaultForwaderContractAddress);
            //initialiasing with the destination address
            await defaultForwarderService.ChangeDestinationRequestAndWaitForReceiptAsync(destinationAddress);
            var destinationInContract = await defaultForwarderService.DestinationQueryAsync();
            //validate the destination address has been set correctly
            Assert.True(destinationInContract.IsTheSameAddress(destinationAddress));

            //Deploying the factory
            var factoryDeploymentReceipt = await ForwarderFactoryService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderFactoryDeployment());
            var factoryAddress = factoryDeploymentReceipt.ContractAddress;
            var factoryService = new ForwarderFactoryService(web3, factoryDeploymentReceipt.ContractAddress);

            //Lets create new invovice to be paid
            var salt = BigInteger.Parse("12"); //12 our invoice number
            var saltHex = new IntTypeEncoder().Encode(salt).ToHex();

            //Calculate the new contract address
            var contractCalculatedAddress = CalculateCreate2AddressMinimalProxy(factoryAddress, saltHex, defaultForwaderContractAddress);



            var transferRecipt = await tokenService.TransferRequestAndWaitForReceiptAsync(contractCalculatedAddress, Web3.Web3.Convert.ToWei(0.001));
            //Check the balance of the adress we sent.. we have not deployed the smart contract so it should be still the same
            var balanceContract = await tokenService.BalanceOfQueryAsync(contractCalculatedAddress);
            Assert.Equal((decimal)0.001, Web3.Web3.Convert.FromWei(balanceContract));

            //Create the clone with the salt to match the address
            var txnReceipt = await factoryService.CloneForwarderRequestAndWaitForReceiptAsync(defaultForwaderContractAddress, salt);
            var clonedAdress = txnReceipt.DecodeAllEvents<ForwarderClonedEventDTO>()[0].Event.ClonedAdress;
            Assert.True(clonedAdress.IsTheSameAddress(contractCalculatedAddress));

            //create a service to for cloned forwarder
            var clonedForwarderService = new ForwarderService(web3, contractCalculatedAddress);
            var destinationInContractCloned = await clonedForwarderService.DestinationQueryAsync();
            //validate the destination address is the same
            Assert.True(destinationInContractCloned.IsTheSameAddress(destinationAddress));

            //Using flush directly in the cloned contract
            //call flush to get all the ether transferred to destination address 
            var flushReceipt = await clonedForwarderService.FlushTokensRequestAndWaitForReceiptAsync(tokenService.ContractHandler.ContractAddress);
          
            //validate balances...
            var newbalanceDestination = await tokenService.BalanceOfQueryAsync(destinationAddress);
            Assert.Equal((decimal)0.001, Web3.Web3.Convert.FromWei(newbalanceDestination));
        }


        [Fact]
        public async void ShouldDeployForwarder_TransferToken_CloneItUsingFactory_FlushTokensUsinFactory()
        {
            var destinationAddress = "0x6C547791C3573c2093d81b919350DB1094707011";
            //Using ropsten infura 
            //var web3 = _ethereumClientIntegrationFixture.GetInfuraWeb3(InfuraNetwork.Ropsten);
            var web3 = _ethereumClientIntegrationFixture.GetWeb3();

            //Deploy our custom token
            var tokenDeploymentReceipt = await ERC20TokenService.DeployContractAndWaitForReceiptAsync(web3,
                new ERC20TokenDeployment() { DecimalUnits = 18, TokenName = "TST", TokenSymbol = "TST", InitialAmount = Web3.Web3.Convert.ToWei(10000) });
            var tokenService = new ERC20TokenService(web3, tokenDeploymentReceipt.ContractAddress);

            //Deploying first the default forwarder (template for all clones)
            var defaultForwarderDeploymentReceipt = await ForwarderService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderDeployment());
            var defaultForwaderContractAddress = defaultForwarderDeploymentReceipt.ContractAddress;
            var defaultForwarderService = new ForwarderService(web3, defaultForwaderContractAddress);
            //initialiasing with the destination address
            await defaultForwarderService.ChangeDestinationRequestAndWaitForReceiptAsync(destinationAddress);
            var destinationInContract = await defaultForwarderService.DestinationQueryAsync();
            //validate the destination address has been set correctly
            Assert.True(destinationInContract.IsTheSameAddress(destinationAddress));

            //Deploying the factory
            var factoryDeploymentReceipt = await ForwarderFactoryService.DeployContractAndWaitForReceiptAsync(web3, new ForwarderFactoryDeployment());
            var factoryAddress = factoryDeploymentReceipt.ContractAddress;
            var factoryService = new ForwarderFactoryService(web3, factoryDeploymentReceipt.ContractAddress);

            //Lets create new salt
            var salt = BigInteger.Parse("12"); //12
            var saltHex = new IntTypeEncoder().Encode(salt).ToHex();

            //Calculate the new contract address
            var contractCalculatedAddress = CalculateCreate2AddressMinimalProxy(factoryAddress, saltHex, defaultForwaderContractAddress);



            //Lets create new salt for another 
            var salt2 = BigInteger.Parse("13"); //13
            var saltHex2 = new IntTypeEncoder().Encode(salt2).ToHex();

            //Calculate the new contract address
            var contractCalculatedAddress2 = CalculateCreate2AddressMinimalProxy(factoryAddress, saltHex2, defaultForwaderContractAddress);


            var transferRecipt = await tokenService.TransferRequestAndWaitForReceiptAsync(contractCalculatedAddress, Web3.Web3.Convert.ToWei(0.001));
            //Check the balance of the adress we sent.. we have not deployed the smart contract so it should be still the same
            var balanceContract = await tokenService.BalanceOfQueryAsync(contractCalculatedAddress);
            Assert.Equal((decimal)0.001, Web3.Web3.Convert.FromWei(balanceContract));

            var transferReceipt2 = await tokenService.TransferRequestAndWaitForReceiptAsync(contractCalculatedAddress2, Web3.Web3.Convert.ToWei(0.001));

            //Create the clone with the salt to match the address
            var txnReceipt = await factoryService.CloneForwarderRequestAndWaitForReceiptAsync(defaultForwaderContractAddress, salt);
            var clonedAdress = txnReceipt.DecodeAllEvents<ForwarderClonedEventDTO>()[0].Event.ClonedAdress;
            Assert.True(clonedAdress.IsTheSameAddress(contractCalculatedAddress));


            var txnReceipt2 = await factoryService.CloneForwarderRequestAndWaitForReceiptAsync(defaultForwaderContractAddress, salt2);
            var clonedAdress2 = txnReceipt2.DecodeAllEvents<ForwarderClonedEventDTO>()[0].Event.ClonedAdress;
            Assert.True(clonedAdress2.IsTheSameAddress(contractCalculatedAddress2));

            //Flushing from the factory
            var flushAllReceipt = await factoryService.FlushTokensRequestAndWaitForReceiptAsync(new List<string> { contractCalculatedAddress, contractCalculatedAddress2 }, tokenService.ContractHandler.ContractAddress);

            //////validate balances... for two forwarders of 0.001 + 0.001
            var newbalanceDestination = await tokenService.BalanceOfQueryAsync(destinationAddress);
            Assert.Equal((decimal)0.001 + (decimal)0.001, Web3.Web3.Convert.FromWei(newbalanceDestination));
        }

        //extracted from latest Nethereum Util
        public static string CalculateCreate2AddressMinimalProxy(string address, string saltHex, string deploymentAddress)
        {
            if (string.IsNullOrEmpty(deploymentAddress))
            {
                throw new System.ArgumentException($"'{nameof(deploymentAddress)}' cannot be null or empty.", nameof(deploymentAddress));
            }

            var bytecode = "3d602d80600a3d3981f3363d3d373d3d3d363d73" + deploymentAddress.RemoveHexPrefix() + "5af43d82803e903d91602b57fd5bf3";
            return CalculateCreate2Address(address, saltHex, bytecode);
        }

        //extracted from latest Nethereum Util
        public static string CalculateCreate2Address(string address, string saltHex, string byteCodeHex)
        {
            if (string.IsNullOrEmpty(address))
            {
                throw new System.ArgumentException($"'{nameof(address)}' cannot be null or empty.", nameof(address));
            }

            if (string.IsNullOrEmpty(saltHex))
            {
                throw new System.ArgumentException($"'{nameof(saltHex)}' cannot be null or empty.", nameof(saltHex));
            }

            if (saltHex.EnsureHexPrefix().Length != 66)
            {
                throw new System.ArgumentException($"'{nameof(saltHex)}' needs to be 32 bytes", nameof(saltHex));
            }

            var sha3 = new Sha3Keccack();
            return sha3.CalculateHashFromHex("0xff", address, saltHex, sha3.CalculateHashFromHex(byteCodeHex)).Substring(24).ConvertToEthereumChecksumAddress();
        }

    }
}