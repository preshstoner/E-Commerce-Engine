// See https://aka.ms/new-console-template for more information
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// 1. Enhanced E-Commerce Jib Model
public record ECommerceJob(
    string JobId,
    string JobName,
    int Priority,
    decimal Amount,
    String CustomerId,
    int RetryCount = 0,
    int MaxRetries = 3
);

// 2. Core Thread-Safe Blocking Priority Queue
public class BlockingPriorityQueue
{
    private readonly PriorityQueue<ECommerceJob, int> _queue = new();
    private readonly object _lock = new();
    private readonly SemaphoreSlim _signal = new(0);

    public void Enqueue(ECommerceJob job)
    {
        lock (_lock)
        {
            _queue.Enqueue(job, job.Priority);
            _signal.Release();
        }
    }

    public async Task<ECommerceJob> DequeueAsync(CancellationToken cancellationToken = default)
    {
        await _signal.WaitAsync(cancellationToken);

        lock (_lock)
        {
            return _queue.Dequeue();
        }
    }
}

// 3. Dead Letter Queue for Failed Payments / Fraud Blocks
public class DeadLetterQueue
{
    private readonly List<(ECommerceJob Job, string Reason)> _failedJobs = new();
    private readonly object _lock = new();

    public void Add(ECommerceJob job, string reason)
    {
        lock (_lock)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"🚨 [DLQ ALERT] Order {job.JobId} ({job.JobName}) routed to DLQ. Reason: {reason}");
            Console.ResetColor();
            _failedJobs.Add((job, reason));
        }
    }

    public IReadOnlyList<(ECommerceJob Job, string Reason)> GetAll()
    {
        lock (_lock)
        {
            return _failedJobs.AsReadOnly();
        }
    }
}

//4. E-Commerce Job Processor (Worker)
public class ECommerceJobProcessor(BlockingPriorityQueue jobQueue, DeadLetterQueue deadLetterQueue)
{
    private readonly BlockingPriorityQueue _jobQueue = jobQueue;
    private readonly DeadLetterQueue _deadLetterQueue = deadLetterQueue;

    public async Task ProcessJobsAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var job = await _jobQueue.DequeueAsync(cancellationToken);
                if (job != null)
                {
                    await ProcessTransactionAsync(job, cancellationToken); 
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessTransactionAsync(ECommerceJob job, CancellationToken ct)
    {
        try
        {
            Console.WriteLine($"⚙️ Processing [{job.JobName}] | Customer: {job.CustomerId} | Amount: ${job.Amount:F2} | Priority: {job.Priority}");

            // Simulate payment gateway or database latency
            await Task.Delay(400, ct);

            // Simulate conditional failures based on business rules
            var randomOutcome = Random.Shared.Next(0, 5);

            if (randomOutcome == 0)
            {
                // Permanent Failure (e.g., Fraud Block or Expired Card) - Should not retry
                throw new InvalidOperationException("Payment Gateway rejected card: Suspected Fraud / Expired.");
            }
            else if (randomOutcome == 1)
            {
                // Transient Failure (e.g., Gateway Timeout) - Should retry 
                throw new TimeoutException("Payment gateway time out connection reset.");
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ SUCCESS: [{job.JobName}] completed for Customer {job.CustomerId} (${job.Amount:F2})");
            Console.ResetColor();
        }
        catch (InvalidOperationException ex)
        {
            // Permanent failure: Senf directly to DLQ without retries
            _deadLetterQueue.Add(job, ex.Message);
        }
        catch (Exception ex)
        {
            // Transient failure: Handle via retry mechanism
            await HandleTransientFailureAsync(job, ex);
        }
    }

    private async Task HandleTransientFailureAsync(ECommerceJob job, Exception ex)
    {
        if (job.RetryCount < job.MaxRetries)
        {
            var retryJob = job with { RetryCount = job.RetryCount + 1 };

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"⚠️ Transient Error on [{job.JobName}]: {ex.Message}. Retrying... (Attempt {retryJob.RetryCount}/{retryJob.MaxRetries}");
            Console.ResetColor();

            // Exponential-ish backoff delay
            await Task.Delay(1000 * retryJob.RetryCount);

            _jobQueue.Enqueue(retryJob);
        }
        else
        {
            _deadLetterQueue.Add(job, $"Exceeded max retries ({job.MaxRetries}). Last error: {ex.Message}");
        }
    }
}

// 5. E-Commerce Job Producer
public class ECommerceJobProducer(BlockingPriorityQueue jobQueue)
{
    private readonly BlockingPriorityQueue _jobQueue = jobQueue;

    public void PublishCheckout(string orderId, string customerId, decimal amount, bool isVip)
    {
        int priority = isVip ? 1 : 2; // VIP jumps ahead of standard checkout
        string name = isVip ? $"VIP-Checkout-{orderId}" : $"Standard-Checkout-{orderId}";

        var job = new ECommerceJob(orderId, name, priority, amount, customerId);

        Console.WriteLine($"🛒 Publishing Checkout -> Order: {orderId} | Priority: {priority} ({(isVip ? "VIP" : "Standard")})");
        _jobQueue.Enqueue(job);
    }

    public void PublishBackgroundJob(string jobId, string taskName, int priority, decimal metadataAmount = 0)
    {
        var job = new ECommerceJob(jobId, taskName, priority, metadataAmount, "SYSTEM");

        Console.WriteLine($"📋 Publishing Background Task -> {taskName} | Priority: {priority}");
        _jobQueue.Enqueue(job);
    }
}

// 6. System Execution Simulation
class Program
{
    static async Task Main(string[] args)
    {
        var queue = new BlockingPriorityQueue();
        var dlg = new DeadLetterQueue();
        var producer = new ECommerceJobProducer(queue);
        var cts = new CancellationTokenSource();

        // Spin up 3 concurrent checkout worker threads
        var workers = new[]
        {
            Task.Run(() => new ECommerceJobProcessor(queue, dlg).ProcessJobsAsync(cts.Token)),
            Task.Run(() => new ECommerceJobProcessor(queue, dlg).ProcessJobsAsync(cts.Token)),
            Task.Run(() => new ECommerceJobProcessor(queue, dlg).ProcessJobsAsync(cts.Token))
        };

        Console.WriteLine("--- SIMULATING E-COMMERCE RUSH HOUR ---\n");

        // Publish a mix of background tasks and checkouts
        producer.PublishBackgroundJob("JOB-SYN-01", "Inventory Sync", 5);
        producer.PublishCheckout("ORD-1001", "CUST-A", 150.00m, isVip: false);
        producer.PublishCheckout("ORD-1002", "CUST-VIP-01", 1250.00m, isVip: true); // VIP Priority 1
        producer.PublishBackgroundJob("JOB-INV-01", "Monthly Invoice PDF Generation", 4, 45.00m);
        producer.PublishCheckout("ORD-1003", "CUST-B", 89.99m, isVip: false);
        producer.PublishCheckout("ORD-1004", "CUST-VIP-02", 3400.00m, isVip: true); // VIP Priority 1

        // Let workers process for 4 seconds
        await Task.Delay(4000);

        // Graceful shutdown
        cts.Cancel();
        await Task.WhenAll(workers);

        Console.WriteLine("\n--- SYSTEM SHUTDOWN CLEAN ---");
        Console.WriteLine($"Total items remaining in DLQ for review: {dlg.GetAll().Count}");

        foreach (var failed in dlg.GetAll())
        {
            Console.WriteLine($" -> Failed Order ID: {failed.Job.JobId} | Reason: {failed.Reason}"); 
        }
    }
}
