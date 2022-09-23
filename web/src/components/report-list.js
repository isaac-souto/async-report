const ReportList = ({ messages, remove }) => {
  return (
    <div className='flex flex-col'>
      <div className='overflow-x-auto sm:-mx-6 lg:-mx-8'>
        <div className='py-4 inline-block min-w-full sm:px-6 lg:px-8'>
          <div className='overflow-hidden'>
            <table className='min-w-full text-center'>
              <thead className='border-b bg-gray-50'>
                <tr>
                  <th
                    colSpan={2}
                    scope='col'
                    className='text-sm font-medium text-gray-900 py-4'
                  >
                    Relatórios Disponíveis
                  </th>
                </tr>
              </thead>
              <tbody>
                {messages.map((item, index) => (
                  <tr key={index} className='bg-white border-b'>
                    <td className='text-sm text-gray-900 font-light px-6 py-2 whitespace-nowrap'>
                      <p className='text-start'>{item.fileName}</p>
                    </td>
                    <td className='text-sm text-gray-900 font-light py-2 whitespace-nowrap'>
                      <div className='flex space-x-2 justify-center'>
                        <button
                          type='submit'
                          className='inline-block px-6 py-2.5 bg-green-500 text-white font-medium text-xs leading-tight uppercase rounded shadow-md hover:bg-green-600 hover:shadow-lg focus:bg-green-600 focus:shadow-lg focus:outline-none focus:ring-0 active:bg-green-700 active:shadow-lg transition duration-150 ease-in-out'
                          onClick={() => window.open(item.url, "_blank")}
                        >
                          Download
                        </button>
                        <button
                          type='submit'
                          className='inline-block px-6 py-2.5 bg-red-600 text-white font-medium text-xs leading-tight uppercase rounded shadow-md hover:bg-red-700 hover:shadow-lg focus:bg-red-700 focus:shadow-lg focus:outline-none focus:ring-0 active:bg-red-800 active:shadow-lg transition duration-150 ease-in-out'
                          onClick={() => remove(item.fileName)}
                        >
                          Remover
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  )
}

export default ReportList
