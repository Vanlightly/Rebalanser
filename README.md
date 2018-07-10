# Rebalanser
Resource distribution amongst collaborating nodes, for example, distributing multiple queues amongst a group of consumers.

## Concepts
Rebalanser is a code library. When a RebalanserContext starts, it acts as a node in a resource group. A resource group is a group of nodes that collaborate to consume a group of resources. An example would be like Kafka's consumer groups where a group of consumers consume the partitions of a given topic.

When a node becomes active it notifies the other nodes. One node is the Coordinator and the rest are Followers. The Coordinator has the job to monitor the coming and going of other consumers and of resources. 

Whenever there is a change in consumers and/or resources, a rebalancing is triggered:
- The Coordinator orders all Followers to stop activity. 
- Once activity has stopped the Coordinator distributes the resources equally between the Followers and itself. 
- The final step is that the Coordinator notifies each Follower of the resources it has been assigned and can start its activity (consuming, reading, writing etc).

Leader election determines who the Coordinator is. If the Coordinator dies, then a Follower takes its place. Coordination between nodes is performed within the Rebalanser code library and a central data store, currently either SQL Server or Hashicorp Consul.

## Providers
There are currently two providers. The SQL Server backend is working though not well tested. The Consul backend is in development. The mechanisms of how rebalancing is implemented is provider specific. Each provider will be documented in the near future.

## Example usage 1 - Adding Kafka's consumer group functionality to RabbitMQ
With Kafka, each partition can only be consumed by a single consumer. Kafka offers automatic assignment of partitions to consumers and performs rebalancing whenever the number of partitions or consumers changes.

Rebalanser can be used to create the same functionality with RabbitMQ. Create a Consistent Hashing Exchange that routes equally between multiple queues (these act as partitions). Deploy multiple consumers and use Rebalanser to perform automatic assignment of queues to consumers whenever the number of queues and consumer change.

```csharp
using (var context = new RebalanserContext())
{
    // this is the first event handler that is called when rebalancing occurs. Once all nodes have stopped consuming, queues are redistributed
    context.OnCancelAssignment += (sender, args) =>
    {
        // cancel subscriptions of existing EventingBasicConsumers
        // ...
    };
    
    // this event handler executes when queues get assigned and consumption can begin.
    context.OnAssignment += (sender, args) =>
    {
        var queues = context.GetAssignedResources();
        foreach (string queue in queues)
        {
            // start an EventingBasicConsumer that subscribes to this queue
            // ...
        }
    };
    
    // start the node with a resource group id, it will become a Coodinator node or a Follower node
    await context.StartAsync("group1");

    // ...
    // ...
}
```
