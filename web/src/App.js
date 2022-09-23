import "react-toastify/dist/ReactToastify.css"
import "./App.css"

import { HubConnectionBuilder, HubConnectionState } from "@microsoft/signalr"
import { ToastContainer } from "react-toastify"
import { useEffect, useState, Fragment } from "react"
import { toast } from "react-toastify"
import { v4 as guid } from "uuid"
import axios from "axios"

import ReportButton from "./components/report-button"
import ReportList from "./components/report-list"
import Header from "./components/header"

function App() {
  const [connection, setConnection] = useState(null)
  const [message, setMessage] = useState(null)
  const [messages, setMessages] = useState([])
  const [userId, setUserId] = useState(null)

  useEffect(() => setUserId(guid()), [])

  useEffect(() => {
    const connect = new HubConnectionBuilder()
      .withUrl("http://localhost:82/notification", {
        withCredentials: false,
      })
      .withAutomaticReconnect()
      .build()

    setConnection(connect)
  }, [])

  useEffect(() => {
    if (message) {
      toast.success(`Arquivo ${message.fileName} disponível para download`)
      setMessages([...messages, message])
      setMessage(null)
    }
  }, [message, messages])

  const connectHub = async () => {
    if (connection) {
      if (connection.state === HubConnectionState.Disconnected) {
        await connection.start()
        await connection.invoke("AddUser", userId)
        connection.on("notification", (message) => {
          setMessage(message)
        })
      }
      await axios.post(`http://localhost:81/api/report/${userId}`)
      toast.info("Relatório solicitado")
    }
  }

  const remove = (fileName) =>
    setMessages(messages.filter((x) => x.fileName !== fileName))

  return (
    <Fragment>
      <ToastContainer autoClose={4000} />
      <div className='App'>
        <div className='py-12 bg-white'>
          <div className='max-w-2xl mx-auto px-4 sm:px-6 lg:px-8'>
            <Header />
            <ReportButton userId={userId} connectHub={connectHub} />
            {messages.length > 0 && (
              <ReportList messages={messages} remove={remove} />
            )}
          </div>
        </div>
      </div>
    </Fragment>
  )
}

export default App
