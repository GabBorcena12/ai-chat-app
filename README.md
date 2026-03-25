📌 ChatApp-Meta-Llama
A simple AI chat application powered by a local Meta Llama 3.1 model using GGUF format. Supports configurable system context, product knowledge, and API integration.

**🚀 Setup Guide**

**1. Download the Model**

Download from Hugging Face:

https://huggingface.co/joshnader/Meta-Llama-3.1-8B-Instruct-Q4_K_M-GGUF/blob/main/meta-llama-3.1-8b-instruct-q4_k_m.gguf


**2. Place the Model File**

Copy the .gguf file to:

AIChatApp/AIChatApp.Core/Models


**3. Configure the Application**

Edit files in:

AIChatApp/AIChatApp.Core/Data

You can configure:

System context

Product knowledge

System limits


**💻 Projects****

🖥️ AIChatApp.Console**

Chat directly with the AI model

Uses configured context and knowledge

Good for testing


**🌐 AIChatApp.API**

Use endpoints to integrate with other projects


**Get Chat History**

GET http://{GATEWAY}/api/chat/history/000001

**Send a Message**

POST http://{GATEWAY}/api/chat/ask

Request Body:

{
  "ChatId": "000001",
  "User": "John Doe",
  "Prompt": "Hi, this is a test message."
}


