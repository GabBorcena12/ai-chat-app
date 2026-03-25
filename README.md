# **ChatApp-Meta-Llama**

A simple AI chat application powered by a local **Meta Llama 3.1** model using **GGUF** format. Supports configurable system context, product knowledge, and API integration.

---

## **Table of Contents**

1. [Requirements](#requirements)
2. [Setup Guide](#setup-guide)

   * [Download the Model](#1-download-the-model)
   * [Place the Model File](#2-place-the-model-file)
   * [Configure the Application](#3-configure-the-application)
3. [Projects Overview](#projects-overview)

   * [AIChatApp.Gateway](#aichatappgateway)
   * [AIChatApp.Console](#aichatappconsole)
   * [AIChatApp.API](#aichatappapi)
4. [API Endpoints](#api-endpoints)

   * [Get Chat History](#get-chat-history)
   * [Send a Message](#send-a-message)
5. [Example Request & Response](#example-request--response)

---

## **Requirements**

* **.NET 9 SDK & Runtime**
* **SQL Server** for database (local or containerized)
* **Meta Llama 3.1 GGUF model file**
* **Postman or any HTTP client** to test API endpoints

---

## **Setup Guide**

### **1. Download the Model**

Download from Hugging Face:
[https://huggingface.co/joshnader/Meta-Llama-3.1-8B-Instruct-Q4_K_M-GGUF/blob/main/meta-llama-3.1-8b-instruct-q4_k_m.gguf](https://huggingface.co/joshnader/Meta-Llama-3.1-8B-Instruct-Q4_K_M-GGUF/blob/main/meta-llama-3.1-8b-instruct-q4_k_m.gguf)

---

### **2. Place the Model File**

Copy the `.gguf` file to the following directory:

```
AIChatApp/AIChatApp.Core/Models
```

---

### **3. Configure the Application**

Edit configuration files in:

```
AIChatApp/AIChatApp.Core/Data
```

You can configure:

* **System context**
* **Product knowledge**
* **System limits**

---

## **Projects Overview**

### **AIChatApp.Gateway**

* Handles request routing to downstream APIs
* Implements middleware:

  * ExceptionHandlingMiddleware
  * IpWhitelistMiddleware
  * RateLimitingMiddleware
  * RequestLoggingMiddleware

### **AIChatApp.Console**

* Chat directly with the AI model
* Uses the configured context and knowledge
* Useful for testing

### **AIChatApp.API**

* Provides endpoints for integration with other projects
* Implements ApiKeyMiddleware and IpWhitelistMiddleware

---

## **API Endpoints**

### **Get Chat History**

```
GET http://{GATEWAY}/api/chat/history/{ChatId}
```

### **Send a Message**

```
POST http://{GATEWAY}/api/chat/ask
```

**Request Body:**

```json
{
  "ChatId": "000001",
  "User": "John Doe",
  "Prompt": "Hi, this is a test message."
}
```

**Additional Request Headers:**

* `X-Api-Client`
* `X-Api-Key`

---

## **Example Request & Response**

**Request:**

```
POST http://localhost:5031/aichat/ask
Headers:
  X-Gateway-Api-Key: gaji-tech-secret-key-gateway-0001
  X-Gateway-Api-Client: GajiTechClient
Body:
{
  "ChatId": "000001",
  "User": "John Doe",
  "Prompt": "Hello AI!"
}
```

**Response:**

```json
{
  "ChatId": "000001",
  "User": "John Doe",
  "Prompt": "Hello AI!",
  "Response": "Hello John! How can I assist you today?"
}
```
