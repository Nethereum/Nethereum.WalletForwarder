// SPDX-License-Identifier: MIT
pragma solidity >=0.5.0 <0.8.0;
import "./Forwarder.sol";

contract ForwarderFactory {

  event ForwarderCloned(address clonedAdress);

  function cloneForwarder(address payable forwarder, uint256 salt)
      public returns (Forwarder clonedForwarder) {
    address payable clonedAddress = createClone(forwarder, salt);
    Forwarder parentForwarder = Forwarder(forwarder);
    clonedForwarder = Forwarder(clonedAddress);
    clonedForwarder.init(parentForwarder.destination());
    emit ForwarderCloned(clonedAddress);
  }

  function createClone(address target, uint256 salt) private returns (address payable result) {
    bytes20 targetBytes = bytes20(target);
    assembly {
      let clone := mload(0x40)
      mstore(clone, 0x3d602d80600a3d3981f3363d3d373d3d3d363d73000000000000000000000000)
      mstore(add(clone, 0x14), targetBytes)
      mstore(add(clone, 0x28), 0x5af43d82803e903d91602b57fd5bf30000000000000000000000000000000000)
      result := create2(0, clone, 0x37, salt)
    }
  }

  function flushTokens(address payable[]  memory forwarders, address tokenAddres) public {
      for (uint index = 0; index < forwarders.length; index++) {
         Forwarder forwarder = Forwarder(forwarders[index]);
         forwarder.flushTokens(tokenAddres);
      }
  }

  function flushEther(address payable[]  memory forwarders) public {
      for (uint index = 0; index < forwarders.length; index++) {
         Forwarder forwarder = Forwarder(forwarders[index]);
         forwarder.flush();
      }
  }

}