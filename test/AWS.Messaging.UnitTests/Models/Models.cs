// Copyright Amazon.com, Inc. or its affiliates. All Rights Reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Concurrent;

namespace AWS.Messaging.UnitTests.Models;

public class ChatMessage
{
    public string MessageDescription { get; set; } = string.Empty;
}

public class AddressInfo
{
    public int Unit { get; set; }
    public string? Street { get; set; }
    public string? ZipCode { get; set; }
}

public class PersonInfo
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public int Age { get; set; }
    public Gender Gender { get; set; }
    public AddressInfo? Address { get; set; }
}

public enum Gender
{
    Male,
    Female
}

public interface IGreeter
{
    string Greet();
}

public class Greeter : IGreeter
{
    public readonly string _message;

    public Greeter()
    {
        _message = Guid.NewGuid().ToString();
    }

    public string Greet()
    {
        return _message;
    }
}

public class TempStorage<T>
{
    public ConcurrentBag<MessageEnvelope<T>> Messages { get; set; } = new ConcurrentBag<MessageEnvelope<T>>();

    public ConcurrentQueue<MessageEnvelope<T>> FifoMessages { get; set; } = new ConcurrentQueue<MessageEnvelope<T>>();
}

/// <summary>
/// An interface representing a message that carries a subject.
/// Used to test polymorphic serialization callbacks registered
/// against an interface rather than a concrete type.
/// </summary>
public interface IMessageWithSubject
{
    string GetSubject();
}

/// <summary>
/// A concrete message type that implements <see cref="IMessageWithSubject"/>.
/// When published as <c>OrderMessage</c>, a callback registered against
/// <c>IMessageWithSubject</c> should still be invoked.
/// </summary>
public class OrderMessage : IMessageWithSubject
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    public string GetSubject() => OrderId;
}

/// <summary>
/// A base class for messages that carry a category.
/// Used to test polymorphic serialization callbacks registered
/// against a base class rather than a concrete type.
/// </summary>
public abstract class CategorizedMessage
{
    public abstract string GetCategory();
}

/// <summary>
/// A concrete message type that extends <see cref="CategorizedMessage"/>.
/// When published as <c>ProductMessage</c>, a callback registered against
/// <c>CategorizedMessage</c> should still be invoked.
/// </summary>
public class ProductMessage : CategorizedMessage
{
    public string ProductName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;

    public override string GetCategory() => Category;
}
