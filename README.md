# E-Commerce Checkout & Payment Processing Engine 🛒⚡

A high-throughput, concurrent background job processing engine built in **C# (.NET)**. This system is designed to handle urgent customer checkouts and payments ahead of low-priority background tasks, ensuring high-value customers and VIP checkouts never experience payment lag.

---

## 🌟 Key Features

* **Priority-Based Scheduling:** Powered by native .NET `PriorityQueue<TElement, TPriority>`. High-priority jobs (like VIP checkouts) automatically jump to the front of the queue.
* **Thread-Safe Concurrency:** Coordinates multiple worker threads safely using `SemaphoreSlim` and `lock` primitives without CPU-wasting busy-waits.
* **Intelligent Error Handling & Retries:** 
  * Transient failures (e.g., payment gateway timeouts) trigger automated exponential backoff retries.
  * Permanent failures (e.g., suspected fraud or expired cards) bypass retries entirely.
* **Dead Letter Queue (DLQ):** Isolates permanently failed or poisoned transactions for manual customer support and engineering review.
* **Graceful Shutdown:** Uses `CancellationTokenSource` to ensure active workers finish gracefully before the system shuts down.

---

## 🏗️ Architecture Overview

| Priority Tier | Job Type | Description |
| :--- | :--- | :--- |
| **Priority 1** | `VIPCheckout` | Instant payment processing for high-value or VIP customers. |
| **Priority 2** | `StandardCheckout` | Standard customer cart checkout and credit card charge. |
| **Priority 3** | `InventoryReservation` | Stock deduction and warehouse hold. |
| **Priority 4** | `InvoiceGeneration` | PDF invoice creation and email dispatch. |
| **Priority 5** | `AnalyticsSync` | Low-priority background sync to data warehouses. |

---

## 🚀 Getting Started

### Prerequisites
* [.NET 8.0 SDK or higher](https://dotnet.microsoft.com/) installed on your machine.

### Running the Application
1. Open your terminal in the project root folder.
2. Run the application using the .NET CLI:
   ```powershell
   dotnet run
